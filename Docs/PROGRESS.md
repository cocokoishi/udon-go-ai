# Progress Ledger

This file records what has actually been established. Planned work is not completion evidence.

## 2026-09-05 — Bound Undo history clearing to the active prefix (static only)

`GoGame.RebuildPositionToMoveCount` now clears only the current/target history
prefix and exposes `lastRebuildHistoryEntriesCleared` for telemetry. The
transactional snapshot/restore path remains unchanged, so failed replay still
restores every authoritative and derived field. No Unity or ClientSim gate was
run.

## 2026-09-05 — Defer MCTS node arrays for dormant tables (static only)

`GoMctsSearch` now allocates its six fixed-capacity node arrays lazily at
`BeginSearch` and releases them with the hidden-table heavy resources. The
capacity and search semantics are unchanged; idle telemetry is null-safe and
memory accounting includes only allocated arrays. No Unity or ClientSim gate
was run.

## 2026-09-05 — Expose independent Black/White custom sliders (static only)

The settings wall now renders separate Black and White custom-visits sliders
and value labels. `GoUI` polls and applies both draft values independently;
the old single-slider field remains hidden only as a serialized compatibility
alias for existing probes/scenes. Apply still commits one synchronized
transaction. No Unity or ClientSim gate was run.

## 2026-09-05 — Normalize every legacy Ultrahard display variant (static only)

`GoUI.NormalizeStaticDifficultyLabel` now converts the old Chinese/bilingual
label, spaced ASCII parentheses, full-width parentheses and dash variants to
the exact product text `Ultrahard`. Preset enums and visit budgets are not
changed; the editor generator already emits `Ultrahard` directly. No Unity or
ClientSim gate was run.

## 2026-09-05 — Mirror superseded references in the documentation archive (static only)

Historical KataGo analysis, project knowledge, Xiangqi porting and real-client
checklist documents are now preserved under
`Docs/Archive/2026-09-pre-refactor/` with their Unity metadata. Their original
paths remain untouched to avoid destructive changes and existing references;
the active `Docs/` index identifies only current contracts. No runtime or Unity
gate was run.

## 2026-09-05 — Align offline calibration tools with current presets (static only)

`Tools/Rules/compare_clientsim_search_calibration.py` and
`Tools/KaTrain/run_corpus_oracle.ps1` now use the current public ladder
`Beginner=4`, `Advanced=32`, `Master=128`, `Ultrahard=384`. The comparison gate
also accepts the packed reader's single completed stage; the retired
four/five-stage threshold would reject valid current exports. Existing
32/64/128/514 files remain historical evidence. No runtime or Unity gate was
run.

## 2026-09-05 — Restrict PvP undo initiation to the current Go player (static only)

`GoGame.CanLocalUndo()` now allows a PvP request only from the seated side
whose turn is active. The other seated side is enabled only when a matching
revision/move-count offer exists, so pressing Undo accepts rather than creates
another request. Stale offers are cleared before a new request is published.
Force Reset remains restricted to the table owner, instance master or AI
controller. This change was statically reviewed; Unity/ClientSim was not run.

## 2026-09-05 — Lock destructive setup changes during live matches (static only)

`GoGame` now rejects New Game, controller/mode changes, starter changes and
side swaps from non-administrators while a match is actively playing. The UI
disables those commands using the same authority predicate; completed games
remain restartable by a seated player. This is a source-level guard only; no
Unity/ClientSim run was performed.

## 2026-09-05 — Neutralize the indoor room palette (static only)

The generator's second wall and ambient colors are now pale warm linen/ivory
instead of blue-green mist. Beige wall, trim and ceiling values were lifted to
keep the three-table room light without changing the dark analysis panels. The
main serialized scene still requires a normal generator run to receive these
material values; no Unity run was performed.

## 2026-09-05 — Normalize legacy difficulty text at the UI boundary (static only)

`GoUI.ApplyStaticLanguage` now maps legacy serialized aliases/casing to the
exact `Ultrahard` display name while leaving the synchronized preset and visit
count untouched. This gives an old scene a safe display fallback; the
validator still rejects stale scene schema/text so regeneration remains
required for release. No Unity run was performed.

## 2026-09-05 — Preserve the superko frequency index on failed rebuild (static only)

Transactional Go rebuilds now snapshot and restore the derived stone-count
frequency array and its validity bit along with the board, hash lanes and move
history. A rejected replay therefore cannot leave an altered or spuriously
invalid fast-reject index. The validator checks both snapshot fields. No
runtime gate was run.

## 2026-09-05 — Release runtime GPU upload mirrors deterministically (static only)

`GoGpuNeuralRuntime.ReleaseInputResources` now destroys the runtime-created
RGBAFloat `Texture2D` upload mirrors before clearing their references. Hidden
table teardown therefore cannot accumulate native texture memory across
worker reacquisition. The production validator checks both release paths; no
Unity gate was run.

## 2026-09-05 — Route real captures through the capture refresh reason (static only)

`GoGame.TryPlayInternal` now emits `REFRESH_CAPTURE` only when the accepted
move actually removed opponent stones; ordinary moves keep `REFRESH_MOVE`.
`GoBoardView` clears stale hover state on every authoritative position change.
This restores the intended single/multi-capture animation gate while keeping
Undo, Reset and network replacement animation-free. No Unity gate was run.

## 2026-09-05 — Slice root ownership telemetry (static only)

The 361 ownership-logit conversions after a packed root readback now resume
across frames with a token/node/revision/owner/hash identity check and a hard
48-point per-Tick quota. MCTS receives the same policy/value/score result only
after the diagnostic mean is complete, preserving the float accumulation order.
Readback accounting is recorded once per result rather than once per telemetry
slice. Roslyn syntax/source-contract checks passed; no Unity or ClientSim run was
performed.

## 2026-09-05 — Stamp base feature group markers (static only)

The base stone/liberty plane pass now uses a generation-stamped marker instead
of clearing another 361-entry array for every leaf. Ladder markers retain their
existing cooperative path. Roslyn syntax/source-contract checks passed; no
Unity or ClientSim run was performed.

## 2026-09-05 — Defer feature-output buffer clears (static only)

`GoFeatureEncoder.BeginEncodeState` no longer clears the previous spatial write
list synchronously when reusing its output buffer. The old touched entries are
processed by the cooperative `CLEAR_SPATIAL` stage with a hard 512-entry quota;
first-use buffers use the same bounded stage. Tensor values are consumed only
after the clear completes. Roslyn syntax/source-contract checks passed; no
Unity or ClientSim run was performed.

## 2026-09-05 — Add bounded authoritative move-mask warm-up (static only)

`GoBoardView.Update` now asks `GoGame.StepMoveMaskCache(24)` to warm the exact
legal/superko mask across frames. `GoGame` hard-clamps each call to 48 points,
uses generation stamps instead of clearing 361 flags on the first hover, and
records cache points/frames/hits/misses plus per-frame maxima. Early hover still
uses the same exact fallback query. Roslyn syntax and source-contract checks
passed; no Unity or ClientSim run was performed.

## 2026-09-05 — Align calibration menu with Ultrahard 384 (static only)

The ClientSim search-calibration menu and method now use `128-384` instead of
the superseded `128-514` label. The standalone difficulty check's diagnostic
message also uses the exact `Ultrahard` product casing. No runtime test was
performed.

## 2026-09-05 — Gate live telemetry on owner lifecycle (static only)

`GoTelemetry.CommitFromController` now rejects a live search tree whose
captured owner lifecycle differs from the controller's current lifecycle. A
callback-order race can therefore keep the persisted final analysis visible
without publishing the previous owner's in-flight tree as current. No Unity
run was performed.

## 2026-09-05 — Reject legacy all-caps generated label (static only)

The loaded-scene validator now rejects a generated UI that contains the old
all-caps `ULTRAHARD` product label, even if a second label happens to contain
the new `Ultrahard` spelling. This makes an old serialized scene fail loudly
until it is regenerated from the current schema-11 generator. No Unity run was
performed.

## 2026-09-05 — Performance telemetry closes on cancellation (static only)

`GoAiController` now finalizes the active frame-time sample when a search is
invalidated or enters an error state, so cancellation cannot leave
`performanceSearchActive` latched or discard the partial sample. The source
validator requires this cleanup path. No Unity or ClientSim run was performed.

## 2026-09-05 — Scene-level current-analysis/title gate (static only)

`GoProductionValidator` now checks loaded TMP labels, not just generator source:
the generated scene must contain the current-position analysis title and the
exact-cased `Ultrahard` product name. A stale schema-9 scene therefore fails
validation until regenerated from the current schema-11 source. No Unity run was
started.

## 2026-09-05 — Single owner-lifecycle event path (static only)

The game ownership callback no longer forwards a duplicate cancellation to the
AI controller. Since both behaviours share the gameplay object, the controller
receives the native ownership event and its authority check handles callback
ordering; the game callback now only schedules recovery. The validator and
networking contract document the one-event/one-generation rule.

## 2026-09-05 — Canonical packed-history validation (static only)

Transactional Go history rebuild now rejects packed move entries with unknown
high bits before replay. Valid location/pass and color bits remain unchanged;
malformed snapshots continue through the existing rollback path instead of
silently decoding to a different move. The production validator requires the
new `IsValidPackedMove` guard. No runtime test was executed.

## 2026-09-05 — Explicit current-position analysis title (static only)

The center screen generator now labels the single analysis surface
`当前局面分析 / CURRENT POSITION ANALYSIS`, and the scene validator checks for
the exact-cased `Ultrahard` text in loaded labels in addition to source checks.
The production scene was not regenerated in this static-only pass.

## 2026-09-05 — Explicit network-recovery probe failure (static only)

The reconnect/leave probe now terminates through its shared `Finish(...)` path
when a recovery invariant fails, instead of leaving `probeFinished` false after
stopping its wait loop. This preserves the failure message and prevents an
editor verifier from misclassifying a real invariant failure as a timeout.
No network or Unity run was performed.

## 2026-09-05 — Random stress failure reporting and current layout docs (static only)

The deterministic `0/32/64/128/256/520`-ply stress probe now reports an
explicit failure when it cannot find a legal move instead of silently waiting
for the case timeout. `GoProductionValidator` requires the hard 90-second
case budget and the explicit failure path. `Docs/ARCHITECTURE.md` now describes
the actual flat Runtime/Tests layout, the sixteen compatibility slots with
three active roots, and the current single-leaf Udon scheduling contract.
No runtime stress or Unity run was performed.

## 2026-09-05 — Fixed-table scheduler clamp (static only)

`GoBoardPool.RefreshGpuSchedule` now derives its scheduling range from the
fixed three-table clamp instead of trusting the synchronized legacy
`visibleTableCount` value. A malformed or old snapshot therefore cannot
re-enable hidden table controllers or inflate active-search accounting. The
production validator requires this source-level clamp; no Unity run was made.

## 2026-09-05 — Direct Ultrahard label and owner-commit guard (static only)

The generated difficulty button now uses the exact product name
`Ultrahard` in both language variants (`Ultrahard / 384 次搜索`), and the
production validator rejects the former all-caps/Chinese aliases. The editor
settings-lifecycle fixture now edits the black custom slider only after its
side is selected, so its 64-visit assertion exercises the real draft path.
`GoAiController` also rejects a completed search whose owner lifecycle changed
before commit, including the callback-order race where a new owner is seen
before an ownership callback. This entry is static-only; Unity and ClientSim
remain NOT EXECUTED.

## 2026-09-05 — Authority-only invalidation boundary (static only)

The presentation layer no longer calls `GoAiController.InvalidateSearch()` for
New Game, Pass, or Resign. `GoGame` now owns those invalidations and performs
them only after its command passes the relevant authority/rule checks. The
same hint and undo paths avoid duplicate cancellation at the continuation
boundary, so a denied spectator action cannot quarantine or rotate another
client's reader. This change was syntax/contract checked only; Unity and
ClientSim remain NOT EXECUTED in this pass.

## 2026-09-05 — Exact history-index and local UI boundary cleanup (static only)

The authoritative game and the MCTS simulation now maintain an exact
stone-count frequency index for the derived hash history. Superko probes skip
the four-lane hash scan only when the candidate stone count is absent; an
incomplete/legacy cache falls back to the original full comparison. The index
is copied with the root prefix and updated for appended simulation moves. The
fixed 101-sample score utility caches only its constant Gaussian weights; all
per-sample score/`Atan` arithmetic is unchanged.

The settings-card black/white selection is local presentation state rather than
an `[UdonSynced]` field. Custom visits remain editable for an authorized table
controller during an active match; applying them still uses the one
`GoAiSettings` transaction and does not interrupt the current SearchSession.
These changes were syntax/contract checked only; Unity and ClientSim were not
run.

## 2026-09-04 — Static lifecycle/hover cleanup (no Unity run)

The current source was statically re-audited after the schema-9 direct
`Ultrahard` label change. `GoGame.revision` is now treated as position/
lifecycle identity while `GoAiSettings.configRevision` remains configuration
identity. Applying or deserializing settings no longer calls
`InvalidateSearch`; the active controller captures its transition quota and
resign threshold at `BeginSearch`, and the next search consumes the new
resolved profile. The MCTS/controller/telemetry stale guards no longer reject a
valid search solely because configuration changed.

The generator source now emits schema `11`: a single center telemetry card
with a real progress fill and omits the old decision-summary/match-notes cards.
Any schema-9 (or older) generated scene must be regenerated in Unity to materialize these
source changes; this static pass did not claim that runtime evidence.

The settings wall now treats preset buttons, Follow Shared/Custom controls and
the custom slider as local black/white drafts. Only `应用档位 · 同步` commits
the complete settings snapshot after an authority check; the two sides retain
independent draft values.

The center screen now contains only the persistent telemetry card and a real
visit-based progress bar; the left `AI 提示` permission/recommendation actions
share one horizontal row. The generator validator rejects the removed
decision-summary, match-notes and obsolete settings labels.

`GoBoardView.SetHoverLocation`/`ClearHover` now use a delta-only
`UpdateHoverPresentation` path for the marker/material/pooled preview stone;
full 361-stone reconciliation remains reserved for authoritative board
refreshes. Changed C# files have balanced braces and the validator now gates
the no-cancellation contract. This pass intentionally did not start Unity or
ClientSim, so runtime compile/performance evidence remains the historical or
OPEN evidence recorded below.

## 2026-09-05 — Unified AI settings synchronization domain (static only)

`GoAiSettings` is now generated once per table and is the sole synchronized
configuration domain. It stores the shared preset, independent black/white
Follow Shared/Custom flags, custom visit values and `configRevision`.
`GoDifficultyProfile` remains as a local resolved search view. The settings
wall's four public preset buttons and four side-mode buttons edit local drafts;
`应用档位 · 同步` commits all five choices through one ownership-checked
`ApplyDraft` transaction and one settings `RequestSerialization()` call.
The current search keeps its captured profile while the next search resolves
the applied snapshot. Generator schema is now `11`; the scene was not rerun in
this static-only pass.

## 2026-09-04 — Current UI, ownership and persistent-analysis follow-up

This is the current follow-up evidence for the checkout after the indoor
three-table change. The latest generator source emits schema `11`; the earlier
follow-up run emitted schema `9` with exactly three visible tables and sixteen
pre-generated backing slots. Each table keeps
its own `GoGame`, UI, telemetry, search and GPU/readback state, so a real
networked room can have a different authoritative owner for each table (one
owner per table; not three owners on a single table). The disposable Unity
project used here is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoValidationFollowup` on
Unity 2022.3.22f1 with the offline license already copied under the allowed
Temp sandbox.

The generated player-facing changes are:

- `Beginner / 入门 = 4`, `Advanced / 进阶 = 32`, `Master / 大师 = 128`,
  `Ultrahard = 384`; Custom is an editable 1–1226 slider
  and commits the exact value through the synchronized Apply action.
- AI Hint permission is a synchronized pre-match opt-in. Start Match freezes
  it for the game; a disabled permission cannot be enabled after start.
- PvP has explicit Start Match while vacant Black/White seat buttons remain
  claimable, allowing the second player to join the other side after the first
  player starts.
- The center card is a persistent analysis template: candidate visits/priors,
  Black/White/no-result win probabilities, `SCORE W-B`, white-positive
  ownership mean, root Q and policy. The owner packs the final root snapshot
  into compact synchronized telemetry, so it remains visible after the move
  and to reconnecting clients.
- `AI MOVE NOW · USE ESTIMATE` commits only a legal estimate already bound to
  the current search identity. The left card starts with the required
  `你好，我是椰子梨梨花。` introduction and technical summary.
- UGUI `GoUiButton.Start` sets `DisableInteractive=true`; the pooled preview
  stone starts below the table and is teleported only while a legal hover is
  shown. Last/ko/hint markers are placed above the flattened stone crown.
- Reset is now authority-gated in both UI and runtime: spectators cannot use
  New Game just because a seat is vacant, and Force Reset is reserved for the
  table owner, instance master, or AI controller. An entered player may use
  the normal New Game flow without gaining the destructive recovery control.
  Undo is a participant action rather than a literal current-turn check:
  either seated PvP player may request, only the opponent may accept, and
  spectators are rejected with an explicit status message.

Fresh ClientSim evidence (raw logs remain in the allowed Temp directory):

The entries below were recorded before the schema-9 direct-label refresh and
remain historical; no Unity rerun was requested for this text-only UI change.

```text
PASS generator v3: schema=7 cells=361 visibleTables=3 ultraHardPresetVisits=384 engineMaxVisits=1226
PASS validator v7: failures=0 warnings=1
PASS custom v5: Button.onClick custom=17, restored Beginner=4, immediate AI move=1, finalAnalysis=True
PASS UI v2: PvP Black/White seats, explicit Start, hint lock, language, draw and analysis/advanced toggle
PASS undo v2: PvAI/PvP undo+draw consent, transactional corruption rollback, undoNoCapture=True
PASS reader v2: A/B/C late-callback recovery, stale token rejection, fresh search completion
PASS runtime v2: exact Beginner searchVisits=4, legal D16, 266 frames, 5.873s ClientSim diagnostic
PASS modes v2: PvP/PvAI/AIvP/AIvAI and 32/128/384 profile checks, restore=4
PASS BoardPool v2: fixed visible=3, capacity=16, initialWorkers=0, dual independent workers=2
PASS floor v2: grounded=True crossedFloor=False
```

The follow-up `GoClientSimPermissionGuardVerifier` entry is included as a
reproducible remote-spectator gate. After the final admin-only Force Reset
refinement, it was not rerun by request; the source/runtime permission chain
was statically reviewed, so this specific multi-client guard remains OPEN.

The runtime timing line is a ClientSim/editor diagnostic, not clean VRChat
client frame-time proof. The fresh three-table graph still has no real
two-client transport/owner-transfer evidence; Quest/Android, model
redistribution rights, Elo strength, and the expensive long-history/high-
budget/complete-game gates remain explicitly open.

## 2026-09-04 — Fixed three-table indoor presentation and preset update

The current product presentation is a compact indoor room with light ivory
and low-saturation blue-grey mist procedural wallpaper, a walkable floor,
soft-white ceiling and collidable room walls.
The generated-scene schema is now 9 so schema-8 scenes must be regenerated
before play to receive the direct Ultrahard label.
Exactly three tables are visible and the add/remove endpoints are retained
only as hidden serialized compatibility bindings. The backing serialized pool
still contains its 16 slots so the existing runtime contracts remain stable.

The current public preset values are now:

```text
Beginner / 入门              4 visits
Advanced / 进阶             32 visits
Master / 大师              128 visits
Ultrahard                    384 visits
```

Older 32/64/128/514 measurements below are historical evidence for the prior
configuration and are not current preset claims.

## 2026-09-04 — Indoor three-table configuration revalidation (historical schema 6; superseded)

The source changes are based on the prior `main` checkpoint
`b4fc9e8a1b4a956d4367deacfe270d1f1a87631c`; this entry records the resulting
indoor configuration change set. The disposable Unity project is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoValidation`, Unity
2022.3.22f1, Windows D3D11, with the offline license copied into the Temp
sandbox. That earlier run generated schema `6`; it is retained as historical
evidence and is superseded by the current schema `7` entry above. It still
covered the compact beige/aqua indoor room, walkable floor/ceiling/walls,
three visible tables, and a 16-slot serialized backing pool.

Real Unity/UdonSharp evidence (raw logs stay in the allowed Temp directory):

```text
PASS  generator: cells=361 visibleTables=3 ultraHardPresetVisits=384
PASS  validator: failures=0 warnings=1 (raw KataGo oracle outside Temp root)
PASS  runtime: exact default Beginner searchVisits=4, legal AI move committed
PASS  modes: PvP/PvAI/AIvP/AIvAI; profile probes 32/128/384; restore 4
PASS  BoardPool: fixed visible=3, capacity=16, two independent searches
PASS  floor: grounded=True crossedFloor=False on Indoor Room Floor · Walkable
OPEN  ReadbackRecovery: ClientSim OOM during Udon serialization before fixture
OPEN  high-budget calibration: UltraHard case timed out at 278 visits
OPEN  long-history stress: 64-ply case exceeded its 90-second case budget
OPEN  complete-game lifecycle: exceeded the 20-minute execution window
OPEN  two/three-table percentile profiler: exceeded its 300-second ClientSim budget
```

The current coalesced final readback contract is `completedStages == 1`.
ClientSim timing is diagnostic and is not clean VRChat-client frame-time proof;
Quest/Android, real two-client transport, model redistribution rights, and Elo
strength remain unverified. The evidence index for this configuration is
`Benchmarks/KaTrain/udon-clientsim-indoor-config-2026-09-04.md`.

## 2026-09-02 — Final remediation checkpoint

`IMPLEMENTED` / `REAL UdonSharp GENERATOR PASS` / `PRODUCTION VALIDATOR PASS`
for the current source checkpoint in the disposable Unity 2022.3.22f1 Temp
project. The generated production scene reported `programs=48 compiled=48
compilerError=False`, `generatedRoots=1`, `udonBehaviours=6514`, and `cells=361`.
The graphics-enabled Production Validator reported `failures=0`; its one
warning only records that the raw KataGo oracle is outside the disposable
Unity project root while the baked asset/manifests were checked.

The production path pre-generates three independent GPU output readers per
table. A cancelled reader enters an explicit `READBACK_QUARANTINED` state and
cannot be reused until its underlying callback is observed; the controller can
rotate A -> B -> C and latches a clear error if the fixed pool is exhausted.
`GoClientSimReaderPoolVerifier` adds the real Udon A/B/C adversarial probe, and
the existing ownership verifier/Production Validator include the third channel.

The leaf legality path now computes an exact combined ordinary-legal and
superko mask once per unchanged `GoSearchState` position. The feature mask and
MCTS expansion reuse that cache. Root reset no longer copies the five full
2049-entry history arrays on every visit; the immutable root prefix is retained
and only the simulated appended tail is overwritten by later paths.

Neural result expansion is now a frame-stepped `PHASE_EXPANDING` state:
legality scan, edge creation, prior normalization, and value backup are
advanced through `StepPendingExpansion` instead of running as one readback
callback. Controller work receives a measured scheduler-derived slice,
ladder transitions use the same measured deadline with an adaptive quota, and a 30-second
no-progress watchdog ends a stalled search explicitly without reducing its
visit budget or fabricating completion. `GoGame` also records actual
`OnPostSerialization` byte counts for later transport measurement, while
`GoBoardPool` retains a fixed local frame-sample ring for percentile reports.
MCTS path selection is also now a `PHASE_SELECTING` state; each search step
replays at most one Go edge and controller admission remains deadline-limited.

The maximum edge arrays and two largest ladder buffers are now lazily allocated
on first use by an active table. The fixed capacities remain unchanged, while
hidden/dormant table slots no longer need to construct the dominant MCTS and
ladder allocations at world startup. The generator schema is now `5`, so a
scene generated with the previous reader/snapshot contract is intentionally
stale and must be regenerated.

The synchronized Go snapshot is now reduced to the authoritative board,
two KataGo previous-position planes, recent moves, packed accepted moves, and
scalar match state. The four 2049-entry hash lanes and stone-count fast-reject
index are local derived caches rebuilt from `moveHistory`; replay is checked
against the received board, both previous planes, recent-move window, and
scalars, and blocks rules/AI if verification fails.
The static array lower bound is therefore `3,141 ints / 12,564 bytes` before
scalar, string, and serialization overhead. Actual `OnPostSerialization` byte
counts and late-join transport behavior remain unverified.

The center analysis card defaults to player-facing output. Tree capacity,
stale-result counters, owner identity, and root-Q diagnostics remain available
only when a diagnostic scene explicitly enables `showDeveloperDiagnostics`.

The graphics-enabled 4-visit ClientSim regression completed with exact
`actualVisits=4`, `readbackRequests=4`, `readbackStages=1`, and `pass=True`.
The latest completed 32-visit sample before the final leaf-ownership trim was
`actualVisits=32`, `elapsedSeconds=12.338`, and therefore failed the hard
`32 <= 11s` gate; the follow-up run for the final trim was intentionally
stopped at the user's request. This is not a production-playability pass.

The following release gates remain intentionally open: random root-search
cases b/d, physical PCVR frame-tail and ergonomics, real two-client transport
and ownership races, repeated unresolved-readback behavior in VRChat, 16-table
memory/load, difficulty strength/Elo, Quest hardware, and inspection of the
exact uploaded/generated release world. The production release verdict remains
`NO` until those evidence layers are closed.

## 2026-09-02 — Exact public difficulty budgets after randomized feature gate

`VERIFIED` for the current generated Udon scene's four public budgets. The
real ClientSim ladder completed exactly `32/32`, `64/64`, `128/128`, and
`514/514` visits after the feature-encoder changes; root visit sums were
`31/63/127/513`, tree edges increased with each budget, and each sample
produced the required root ownership readback:

```text
PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=32,64,128,514
PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True
```

The diagnostic times were `8.695s`, `16.044s`, `32.572s`, and `128.138s` in
this run. They are not substituted for the clean-host `32 <= 11s` and
`64 <= 20s` performance gate. The full record is
`Benchmarks/KaTrain/udon-clientsim-difficulty-post-random16-2026-09-02.md`
with its JSON companion; the Unity log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-difficulty-post-random16-v1.log`.
This still does not establish monotonic playing strength or Elo calibration.

## 2026-09-02 — Real Udon 16-state randomized feature differential

`EQUIVALENCE VERIFIED` for the expanded feature corpus. The compiled UdonSharp
probe now generates the move histories inside ClientSim, one move or encoder
slice per Udon frame: seven existing production boundary states, an empty
Chinese-rules control, and eight deterministic random histories (Tromp-Taylor
and Chinese rules at 32/64/96/144 plies). Fresh NPZ inputs from the authorized
KaTrain KataGo executable match every captured Udon feature value:

```text
PURE_UDON_GO_CLIENTSIM_FEATURE_RESULT passed=True cases=16 ...
PURE_UDON_GO_CLIENTSIM_FEATURE_PASS cases=16 spatialFloats=127072 globalFloats=304
PURE_UDON_GO_KATAGO_FEATURE_ORACLE_PASS cases=16 ...
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=16 failures=0 tolerance=1e-06
```

The report is
`Benchmarks/KaTrain/udon-clientsim-feature-differential-random16-2026-09-02.md`
with its JSON companion. The Udon log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-feature-random16-v1.log`.
This is randomized-history feature equivalence, not exhaustive rules/search
equivalence or strength calibration.

## 2026-09-02 — Nested ladder rollback and post-fix Udon neural gates

`EQUIVALENCE VERIFIED` for the fixed four-root neural corpus and seven-state
KataGo INPUTSVERSION 7 feature corpus after optimizing the ladder encoder.
The encoder now avoids repeated full-board copies during group/liberty probes
and uses nested per-depth sparse rollback. A review caught that a child sparse
snapshot could otherwise leak mutations into a sibling branch; the rollback
path now restores every active descendant before the parent branch. This fix
was exercised through the real compiled UdonSharp program in D3D11 ClientSim.

```text
PURE_UDON_GO_CLIENTSIM_NEURAL_OUTPUT_EXPORT_PASS cases=4 visits=32 rootStages=5 numericComparison=not-performed
PURE_UDON_GO_NEURAL_NUMERIC_COMPARISON_PASS cases=4 heads=5 atol=0.0001 rtol=1e-05 tensorExact=True
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=7 failures=0 tolerance=1e-06
PURE_UDON_GO_CLIENTSIM_PASS ... searchVisits=32 ... encodeGeneration=32 ...
```

The neural numeric report is
`Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-rollback-2026-09-02.md`;
the feature report is
`Benchmarks/KaTrain/udon-clientsim-feature-differential-rollback-2026-09-02.md`.
The real Udon logs are
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-neural-fixed-rollback-v1.log`,
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-feature-rollback-v1.log`,
and
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-runtime-rollback-v1.log`.
The bundled model converter suite also passed `12` Python tests.

The one-click generator and validator were rerun after the source change in
the same isolated Unity project:

```text
PURE_UDON_GO_GENERATOR_PASS ... cells=361 ... ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT ... generatedRoots=1 udonBehaviours=6498 programs=50 compiled=50 compilerError=False
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 ... maxVisits=1226
```

The generation and validation logs are
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-rollback-generate-v1.log`
and
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-rollback-validator-v1.log`.
The single warning is the expected raw-oracle-outside-Temp-root warning; the
committed baked weights and manifests were validated.

The randomized root-search stress extension is intentionally not reported as
passed: post-fix `random-case-b` exceeded the single-case 90-second ladder
diagnostic budget, while `random-case-d` has no completed post-fix export and
also exceeded that budget in an earlier stress run. Randomized full-input
neural equivalence and worst-case ladder scheduling therefore remain open
gates.

## 2026-09-02 — Real ClientSim gravity/floor settling gate

`VERIFIED` for the generated environment's local locomotion smoke path. A
compiled UdonSharp probe teleported the ClientSim local player to `y=2` over
the actual generated gallery-floor collider, then observed the real
`VRCPlayerApi` position, velocity, and grounded state from Udon. It did not
pass by inspecting a collider in the editor:

```text
PURE_UDON_GO_CLIENTSIM_FLOOR_RESULT pass=True startY=2 finalY=0.004999951 minY=0.004999951 floorY=6.661338E-17 grounded=True sawDrop=True crossedFloor=False stableFrames=12 sampleFrames=13 finalVelocityY=0 failure=
PURE_UDON_GO_CLIENTSIM_FLOOR_PASS
PURE_UDON_GO_CLIENTSIM_FLOOR_FINAL pass=True
```

The player descended, remained above the floor, and stayed grounded for 12
consecutive observed frames. Unity ran with `-force-d3d11` and the copied
license file; the batch process returned `0`. The source probe is
`Assets/PureUdonGo/Tests/GoFloorGravityProbe.cs`, the editor harness is
`Assets/PureUdonGo/Editor/GoClientSimFloorVerifier.cs`, and the log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-floor-v4.log`.

This proves the generated floor collision path in ClientSim, not physical
VRChat/Quest locomotion. Independent multi-client transport, balanced
strength calibration, and physical headset validation remain separate gates.

## 2026-09-02 — Real Udon 361-point legal-move matrix differential

`EQUIVALENCE VERIFIED` for eleven production rule-state checkpoints. A
compiled UdonSharp probe replayed the fixed 160-action history and queried the
real `GoGame.IsRulesLegalMove()` API at every one of 361 intersections plus
`PASS` at action counts `0, 1, 2, 4, 8, 16, 32, 64, 96, 128, 160`. The
independent Python reference compared every board, legal mask, pass result,
side/state/counter scalar, and final history count:

```text
PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_PASS actions=160 positions=11 legalQueries=3971 referenceComparison=not-performed
PURE_UDON_GO_RULES_LEGAL_MATRIX_DIFFERENTIAL_PASS positions=11 queries=3971 accepted=160
```

All rows passed with legal counts `361, 360, 359, 357, 353, 345, 329, 298,
266, 236, 207`. The full method and scope are recorded in
`Benchmarks/KaTrain/udon-clientsim-rules-legal-matrix-differential-2026-09-02.md`;
the Unity log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-rules-matrix-v1.log`.
Broader randomized histories, handicap matrices, and exhaustive 19×19 state
coverage remain open rather than being implied by this finite corpus.

## 2026-09-02 — Reader-pool-aware generator and validator regression

`VERIFIED` after the stale-readback quarantine change. The current repository
source was run twice through the real Unity one-click generation path in a
fresh Temp isolation project. Both runs kept one generated root, `6498`
UdonBehaviours, `46/46` compiled UdonSharp programs, and no compiler error:

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity root=PureUdonGo.ProductionRoot cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=6498 programs=46 compiled=46 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170e-b10c128-s1141046784-d204142634 maxVisits=1226
```

The validator now checks the intentional `16 tables × 2 readback channels`
layout: reader A and the stale-callback quarantine reader B are different and
unique to their table, while each pair shares only that table's GPU runtime.
The full report is `Benchmarks/KaTrain/udon-clientsim-post-recovery-generation-2026-09-02.md`
with its JSON companion. The one warning is only that the raw KataGo oracle is
outside the isolated Unity project root; baked weights and manifests passed.

## 2026-09-01 — Real Udon remote-leave recovery for seats/offers/AI authority

`VERIFIED` for the covered owner-side recovery path. A real UdonSharp probe
prepared three generated tables: two one-move PvP positions with pending undo
and draw offers, plus an AI-AI table with a synchronized controller authority.
The ClientSim verifier spawned a real remote player, bound the test identities
to that player's ID inside the Udon fixture, removed that remote player, and
read the cleanup result from Udon after the actual `OnPlayerLeft` callback:

```text
PURE_UDON_GO_CLIENTSIM_RECOVERY_RESULT prepared=True probePassed=True undoSeatAfter=-1 drawSeatAfter=-1 undoOfferCleared=True drawOfferCleared=True aiAuthorityAfter=-1 recovery=0->1,0->1,0->1 failure=
PURE_UDON_GO_CLIENTSIM_RECOVERY_PASS
PURE_UDON_GO_CLIENTSIM_RECOVERY_FINAL pass=True
```

The positions stayed in `STATE_PLAYING` with their committed opening move,
both human seat identities and both offers were cleared, the AI-AI authority
was cleared, and each table incremented `networkRecoveryRevision`. The full
report is `Benchmarks/KaTrain/udon-clientsim-network-recovery-2026-09-01.md`
and its JSON companion; the Unity log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-network-recovery-v4.log`.
The remote leave event is real, but the test still has one Udon VM; independent
VRChat-client transport, remote snapshots, and new-player-ID reconnect remain
open.

## 2026-09-01 — Owner-authoritative readback quarantine and hand-back recovery

`VERIFIED` for the covered real UdonSharp/ClientSim ownership lifecycle. The
Xiangqi-style snapshot lifecycle was tightened so non-owner
`OnDeserialization` only invalidates local search, clears hover, and refreshes;
only the eventual owner sanitizes and publishes state during delayed recovery.
PvP start now also follows the owner/revision/serialization path, and AI-AI
mode transitions do not leave stale control-authority fields behind.

The runtime found and fixed a real async edge case during this milestone:
ClientSim can cancel an in-flight VRC readback without delivering its stale
callback. Reusing that reader would leave `discardedCallbacks` quarantined
forever. Each generated table now has a second pre-generated reader; the old
callback channel remains isolated while the new owner searches through the
alternate channel. The real D3D11 run proves remote ownership cancellation,
no old-owner move, owner lifecycle invalidation, active-reader rotation,
alternate-reader ownership return, and a fresh 32-visit AI move:

```text
PURE_UDON_GO_CLIENTSIM_OWNER_REMOTE_PASS ownerId=2 revision=2 moveCount=0 aiMoves=0 staleCallbacks=0 cancelled=True searchPhase=4 readerState=4
PURE_UDON_GO_CLIENTSIM_OWNER_PASS remoteOwnerId=2 localOwnerId=1 oldMoveCount=0 finalMoveCount=1 oldAiMoves=0 finalAiMoves=1 visits=32 readerCompletedStages=4 ownershipReadbacks=1 readerRotated=True alternateReaderReturned=True ownerLifecycle=6
PURE_UDON_GO_CLIENTSIM_OWNER_RESULT pass=True
```

The generated scene was recompiled after the reader-pool change with
`generatedRoots=1`, `udonBehaviours=6498`, `programs=45`, `compiled=45`, and
`compilerError=False`. The complete report is
`Benchmarks/KaTrain/udon-clientsim-ownership-reader-pool-2026-09-01.md` and
its JSON companion. The evidence remains a one-Udon-VM ClientSim simulation;
independently running VRChat clients and real network transport remain open.

## 2026-09-01 — Real Udon public-ladder search/oracle comparison

`VERIFIED` for 16 position-level observations and exact public visit
accounting; `NOT STRENGTH CALIBRATED`. Real UdonSharp/ClientSim runs evaluated
the same four fixed opening/capture/fight/mixed positions at 32/64 and
128/514 visits in separate low/high batches. All 16 samples completed their
exact target visits, had non-zero selected-root visits, and produced valid
readback/revision evidence. The high-profile run was repeated after the
harness caught and fixed a real profile-start reset bug; the invalid mixed
32/64-after-first-scenario run is not used as evidence.

The independent comparator matched every Udon selected move to the
corresponding same-production-model KataGo response: `16/16` exact visits and
`16/16` selected moves present in the oracle candidate lists. The observed
mean oracle ranks were Beginner `2.00`, Advanced `2.50`, Master `3.75`, and
UltraHard `4.75`, with top-1 agreement `4/16`. Because the rank trend was not
monotonic, this is explicitly not a strength claim:

```text
PURE_UDON_GO_SEARCH_ORACLE_COMPARISON_PASS samples=16 exactVisits=16 oracleCoverage=16 strengthCalibration=not-established
```

Evidence is committed in
`Benchmarks/KaTrain/udon-clientsim-search-oracle-comparison-2026-09-01.md`
and its JSON report. Udon logs are
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-search-calibration-32-64-v4.log`
and
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-search-calibration-128-514-v5.log`.
This does not establish whole-search KataGo identity, Elo, rank, monotonic
strength, or balanced complete-game win rate.

## 2026-09-01 — Final generator revalidation after Udon probes

`VERIFIED` for the one-click production generation path after adding the
rules and search calibration probes. Two independent real Unity batchmode
runs executed `GenerateAndVerifyRealUdonWorld` on the isolated project and
both reported:

```text
PURE_UDON_GO_GENERATOR_PASS ... cells=361 ... ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT ... generatedRoots=1 udonBehaviours=6482 programs=45 compiled=45 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

The count of 45 includes all imported temporary verifier program assets; the
generated production scene itself contains one owned `PureUdonGo.ProductionRoot`
and the same 16-table serialized pool on both runs. The second generation
repaired the existing root in place rather than adding a second root. The
scene text hash is not used as an idempotence assertion because Unity's
serializer rewrites object-file ordering/metadata between saves.

The subsequent real `GoProductionValidator.ValidateFinalProductionScene`
passed with `failures=0`; its one warning only reports that the raw KataGo
oracle file is outside the isolated Unity project root while the committed
baked asset and manifests were checked. Unity license evidence in both logs
contains `Successfully resolved entitlements`:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170-b10c128-s1141046784-d204142634 maxVisits=1226
```

Logs:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-final-generate-v6.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-final-generate-v7.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-validator-v8.log`

This establishes generator/compile/structural validation, not physical
VRChat or Quest runtime behavior.

## 2026-09-01 — Real Udon 160-action rules/history differential

`EQUIVALENCE VERIFIED` for a fixed long-history corpus. A newly compiled
UdonSharp probe ran 160 production `GoGame` actions in ClientSim, one action
per Udon frame, and exported every 361-point current board plus both complete
`previousBoard1`/`previousBoard2` history boards and rule-state scalars. The
sequence included four captures and three separated passes and ended with
`moveCount=160`, `positionHistoryCount=161`, `blackCaptures=4`,
`whiteCaptures=4`, and a playing state.

The independent Python replay compared all 160 rows and all three board
snapshots per row against `Tests/Reference/go_rules_reference.py`:

```text
PURE_UDON_GO_CLIENTSIM_RULE_TRACE_PASS moves=160 accepted=160 referenceComparison=not-performed
PURE_UDON_GO_RULES_NUMERIC_DIFFERENTIAL_PASS moves=160 rows=160 accepted=160
```

The full report is committed at
`Benchmarks/KaTrain/udon-clientsim-rules-differential-2026-09-01.md` and
`Benchmarks/KaTrain/udon-clientsim-rules-differential-2026-09-01.json`.
The Unity evidence log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-rules-trace-v1.log`.
The comparator has an explicit protocol mapping for Python's no-ko `-1` to
production `GoGame.NONE=-2`; this is serialization normalization, not a
gameplay exception. The corpus does not claim exhaustive legal matrices,
randomized histories, full territory cleanup, or network transport parity.

## 2026-09-01 — Real Udon ClientSim neural head numeric equivalence

`EQUIVALENCE VERIFIED` for four fixed root positions captured from the real
serialized Udon path. The temporary probe was compiled as UdonSharp and ran in
ClientSim; it used the production frame-scheduled feature encoder, GPU shader
graph, asynchronous readback, and 32-visit PUCT controller. Before each root
GPU evaluation it copied the completed `22 x 19 x 19` spatial and 19-channel
global feature arrays from the real Udon encoder into the export. The export
verifier itself is intentionally transport/completeness-only:

```text
PURE_UDON_GO_CLIENTSIM_NEURAL_OUTPUT_EXPORT_PASS cases=4 visits=32 rootStages=5 numericComparison=not-performed
PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_RESULT pass=True
```

The independent `Tools/NNReference/compare_clientsim_neural_export.py` tool
then loaded the committed raw model and baked atlas, proved all `127` raw /
baked tensors exact, ran the CPU graph over those captured Udon features, and
compared policy spatial, policy pass, value, score, and ownership raw outputs.
The four cases passed with `atol=1e-4`, `rtol=1e-5`, zero mismatches, and a
global head maximum absolute error of `9.5367431640625e-06`:

```text
PURE_UDON_GO_NEURAL_NUMERIC_COMPARISON_PASS cases=4 heads=5 atol=0.0001 rtol=1e-05 tensorExact=True
```

Evidence and the complete per-case report are committed in
`Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-2026-09-01.md`
and
`Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-2026-09-01.json`.
The Unity log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-neural-output-export-v4.log`;
the persistent ClientSim export is under the allowed Temp verification
project. This does not claim randomized full-input coverage, search-strength
calibration, Quest/backend equivalence, or whole-search equivalence to the
KataGo executable.

## 2026-09-01 — Undo history replay parity and AIvAI panel orientation

`VERIFIED` for the two audit findings addressed in this milestone. `GoGame`
`ReplayMove()` now snapshots the pre-move board before applying a replayed
placement, matching the normal `TryPlayInternal()` convention. This preserves
the exact `previousBoard1`/`previousBoard2` history planes consumed by the
KataGo feature encoder after PvAI undo. The regression is executed through the
serialized Udon probe in ClientSim, not a native rules substitute:

```text
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_RESULT pvAiUndo=True preHistory=True postHistory=True pvAiUndoHistory=True ...
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_PASS
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_RESULT pass=True
```

The same probe still passes PvP undo-request protection and PvP draw-offer
protection. Evidence:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-replay-history-v9.log`.

`GoUI` now uses the product rule `PvP = 90° side wall` and
`PvAI/AIvP/AIvAI = 0° rear wall`; the previous expression incorrectly put
AIvAI into the side-facing layout. The real Unity Button/ClientSim UI probe
now changes the generated mode through `UnityButton.onClick` and records:

```text
PURE_UDON_GO_CLIENTSIM_UI_AIVAI_PASS panelYaw=0
PURE_UDON_GO_CLIENTSIM_UI_PVP_PASS panelYaw=90
PURE_UDON_GO_CLIENTSIM_UI_HINT_PASS ... visits=32 ... ownershipReadbacks=1
PURE_UDON_GO_CLIENTSIM_UI_RESULT pass=True
```

Evidence:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-aivai-v1.log`.
The regenerated isolated scene also passed the real UdonSharp compile/world
gate (`generatedRoots=1`, `udonBehaviours=6482`, `compiled=41`,
`compilerError=False`; the extra program is the temporary ClientSim probe).
No push was performed.

## 2026-09-01 — Verifier semantics tightened and rules probe frame-scheduled

`VERIFIED` for verifier honesty and the additional exact scoring assertion.
The ClientSim neural entry point is now presented as `Export ClientSim Neural
Outputs`; its JSON schema is
`pure-udon-go.clientsim-neural-output-export.v2` and explicitly records
`numericComparison=NOT_PERFORMED_BY_THIS_VERIFIER`. Its PASS means that the
serialized Udon encoder/GPU/readback/PUCT path returned complete root policy,
value, score, and ownership arrays. It is not a KataGo numerical comparison.

The former position-quality entry point is now `Verify ClientSim PUCT Search
Smoke`, and the former complete-game entry point is now `Verify ClientSim
Complete AI Game Smoke`. Their current PASS markers explicitly state that
strength calibration was not performed. Existing benchmark logs with the old
marker names remain historical records; they must be read using the scope
statements in their benchmark documents, not as Elo, policy correctness, or
KataGo numeric equivalence.

The real PUCT smoke run still passed after the rename:

```text
PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_SAMPLE ... actualVisits=32 ... rootVisitSum=31 ...
PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_PASS scenarios=4 visits=32 policyPuctDivergence=true strengthCalibration=not-performed
PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_RESULT pass=True
```

The Rules probe now runs one scenario per Udon `Update` event, preventing the
10-second Udon VM event budget from turning a finite suite into a timeout. It
passed through real ClientSim with exact scoring evidence:

```text
PURE_UDON_GO_CLIENTSIM_RULES_RESULT capture=True doublePass=True exactAreaScore=True suicide=True ko=True simpleKo=True searchKo=True multiSuicidePrisoner=True pureRulesApi=True capacity=True handicap=True resign=True ... doublePassArea=1:1 doublePassScore=7.5 ...
PURE_UDON_GO_CLIENTSIM_RULES_PASS
PURE_UDON_GO_CLIENTSIM_RULES_RESULT pass=True
```

The same frame-scheduled probe now includes a real triple-ko cycle. It builds
the setup with PSK disabled, enables positional superko for the cycle, and
checks that the sixth move is rejected even though it is not the current
simple-ko point; with PSK disabled again, the same move is accepted. This is
also the observed contract of the implementation: disabling positional PSK
does not disable the independent simple-ko check.

```text
PURE_UDON_GO_CLIENTSIM_RULES_RESULT ... simpleKo=True searchKo=True positionalSuperko=True ... failure=
PURE_UDON_GO_CLIENTSIM_RULES_PASS
```

Evidence:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-psk-rules-v1.log`.

Evidence logs:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-neural-output-export-v3.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-puct-smoke-v1.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-verifier-semantics-rules-v2.log`

The neural export JSON was also retained outside Unity's self-cleaned Temp
root at:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSim20260831\Assets\udon-go-ai\Benchmarks\KaTrain\ClientSimExports\GoNeuralOutputExport\udon-neural-output-export.json`.
It contains `schema=pure-udon-go.clientsim-neural-output-export.v2`,
`status=CLIENTSIM_OUTPUT_EXPORT_PASS`, four 32-visit cases, five root
readback stages, and `numericComparison=NOT_PERFORMED_BY_THIS_VERIFIER`.

The bundled Python reference suite also passed independently: `15` tests,
including Go rules and KataGo INPUTSVERSION 7 feature contracts, `OK`.

These changes correct the meaning of the evidence; they do not claim that
the remaining strength calibration, full territory cleanup, or end-to-end
performance gates are complete.

## 2026-09-01 — Xiangqi-style one-row gallery, single board input path, and UI refresh repair

`VERIFIED` for the generated-world layout, source binding, real Udon compile,
ClientSim runtime, UI event path, and multi-table scheduler regression. The
production generator now follows the authorized Pure Udon Xiangqi composition
for the table gallery: all 16 pre-generated roots share one row, each adjacent
root is `3.75 m` apart at the current 60% world scale, and the generated floor
is approximately `61.2 m` long with a non-trigger collider. The table height is
lowered to the 76% profile, and the Go stone pool uses a `0.38 × 0.17 × 0.38`
unit rounded-sphere profile whose bottom remains on the maple/ink datum.

The old duplicate board interaction surface was removed. Each of the 361 Go
points now has only its collider plus `GoBoardCell` Udon endpoint; the generated
UGUI Buttons are limited to the 31 control-wall actions per table. The clean
generated world therefore reports `6482` Udon behaviours (down from the prior
duplicate-grid output's `12258`) and `40/40` compiled Udon program assets. The
control wall remains a three-screen world-space UGUI/TMP surface, while its
left Match screen is compact and state-driven; redundant per-colour controller
toggles are retained only as hidden serialized compatibility endpoints, with
the four primary mode buttons exposed to players.

GoUI now writes only changed TMP values, GoTelemetry owns the center analysis
text instead of competing with GoUI, and the AI controller throttles
presentation/telemetry refreshes without throttling search transitions. This
removes the observed alternating-TMP write pattern. The runtime evidence
reported `uiRefreshCount=6`, `telemetryRefreshCount=105`, and
`schedulerObservedFps=67.2` during a complete 32-visit turn. This is a refresh
and scheduling regression result, not a claim that the formal end-to-end
performance gate has passed: the same run measured `27.214 s` for 32 visits,
which remains above the current `11.0 s` acceptance target and keeps the
performance milestone open.

The stale `GoBoardPool` Udon program source reference was repaired in the
production asset and the generator now repairs null or old `.cs.meta` source
references before compiling. Real Unity evidence:

```text
PURE_UDON_GO_GENERATOR_PASS ... cells=361 ... ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT ... generatedRoots=1 udonBehaviours=6482 programs=40 compiled=40 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 ... maxVisits=1226
```

The same output path was generated a second time with the same root, behaviour,
program, and compile counts, with no duplicate-name or C#/Shader/Udon runtime
errors. Logs:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-generate-v8.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-idempotence-v1.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-validator-v2.log`

The real graphics-backed ClientSim checks also passed:

```text
PURE_UDON_GO_CLIENTSIM_PASS ... searchVisits=32 ... aiMoves=1 ... lastMove=D16 ... encodeGeneration=32 ... rootEdges=362 rootPositive=2 rootVisitSum=31 rootMaxVisits=28
PURE_UDON_GO_CLIENTSIM_UI_HINT_PASS ... hintMove=Q16 ... baselineMoveCount=0 finalMoveCount=0 ... visits=32 ... ownershipReadbacks=1
PURE_UDON_GO_CLIENTSIM_POOL_PASS visible=3->4->3 tables=16 dualSearches=2 schedulerSlots=0,1
```

Logs:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-runtime-v3.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-clientsim-ui-v5.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-row-ui-pool-v1.log`

These results are real UdonSharp/ClientSim execution; the editor side only
dispatches serialized Udon events and reads the resulting Udon variables. No
push was performed.

## 2026-09-01 — Public 32/64 Udon performance gates and external feature check

`VERIFIED` for the exact public `Beginner` and `Advanced` search profiles in
the isolated Unity 2022.3.22f1 project. These are real UdonSharp-compiled
programs executed by VRChat ClientSim; the editor verifier only dispatches a
Udon event and reads serialized Udon variables. It does not substitute a
native C# rules/search run.

After the fresh-leaf guard, bounded feature slices, legal-move caching,
history stone-count filter, stamped group/liberty probes, incremental probe
rollback, and output-readback optimization, the final runs recorded:

```text
Beginner: targetVisits=32 actualVisits=32 elapsedSeconds=8.488 searchMs=7429.627 freshFeatureEncoding=True readbackRequests=129
Advanced: targetVisits=64 actualVisits=64 elapsedSeconds=15.334 searchMs=14280.700 freshFeatureEncoding=True readbackRequests=257
```

Both runs emitted `PURE_UDON_GO_CLIENTSIM_PERF_PASS` and
`PURE_UDON_GO_CLIENTSIM_PERF_RESULT pass=True`. The public profiles therefore
meet the current local gates of `32 <= 11s` and `64 <= 20s` in this measured
environment; the measured search time is also below `0.30s/visit` for both
samples. Full-log diagnostics found no C# compiler, Shader, UdonSharp, or
unhandled runtime error. Exact log paths and stage accounting are recorded in
`Benchmarks/KaTrain/udon-clientsim-performance-2026-09-01.md`.

The Udon feature fixture was also compared against a fresh execution of the
authorized KaTrain KataGo executable and the bundled production model:

```text
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=7 failures=0 tolerance=1e-6
```

This is `EQUIVALENCE VERIFIED` for `55,594` spatial and `133` global values;
oracle hashes and the exact disposable report path are in
`Benchmarks/KaTrain/udon-clientsim-feature-differential-2026-09-01.md`.
This checkpoint does not claim VRChat device runtime, multi-client network
runtime, or KaTrain strength calibration.

## 2026-09-01 — Optimized Udon visit-count and move-quality regression

`VERIFIED` for the exact public Udon search budgets after the performance
optimization. Real Unity/ClientSim runs completed `32/32`, `64/64`, `128/128`,
and `514/514` visits; elapsed times were `8.488s`, `15.334s`, `27.892s`, and
`106.394s`. Every run emitted `freshFeatureEncoding=True` and
`PURE_UDON_GO_CLIENTSIM_PERF_RESULT pass=True`. The full measurements and
logs are in
`Benchmarks/KaTrain/udon-clientsim-optimization-regression-2026-09-01.md`.

The current four-position Udon quality corpus also passed. Its selected moves
and tree-edge counts match the prior Udon record exactly (`Q16`, `Q4`, `R16`,
`Q16`; `11458`, `11432`, `11294`, `11172`), and the same four coarse KaTrain
oracle orders remain `0`, `2`, `9`, and `2`. The root ownership readback is
present; non-root leaves correctly use four readback stages. This is evidence
of no observed regression in the tested corpus, not a claim of globally
unchanged Elo or strength: the authorized oracle uses a different model and a
balanced complete-game calibration is still open. The raw production model
hash and feature differential provenance are recorded in the benchmark file.

The public `Verify ClientSim Difficulty Search Ladder` was then run through
the generated UI events. It passed all four profiles with exact
configured/completed visits `32/32`, `64/64`, `128/128`, and `514/514`; the
root ownership readback counter advanced once per sample. The old verifier's
five-stage assertion was corrected to the optimized four-stage non-root path,
while retaining the root ownership requirement. The terminal marker was
`PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True`; the exact log and table are
in the same optimization regression benchmark. This confirms the public
profile-to-search wiring, not a global playing-strength calibration.

## 2026-09-01 — Go undo/draw consent and real Button.onClick path

`VERIFIED` for the covered real Udon/ClientSim paths. Go now keeps a compact
packed accepted-move log, so PvAI/AIvP undo can replay the position and restore
the complete board/history/hash state instead of guessing from the last two
boards. PvP `RequestUndo` is an offer requiring the opposite seated player to
accept; PvP `OfferOrAcceptDraw` follows the same synchronized offer tuple and
cannot be accepted by its requester. New moves, resets, mode changes, seat
changes, terminal states, and disconnect repair clear pending offers.

The multi-frame Udon probe recorded:

```text
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_RESULT pvAiUndo=True pvpUndoConsent=True pvpDrawConsent=True failure=
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_PASS
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_RESULT pass=True
```

The generated control wall now exposes dynamic `UNDO`/`REQUEST UNDO` and
`DRAW`/`OFFER DRAW` labels. The UI probe invokes the actual Unity
`Button.onClick` persistent Udon listeners and passed PVP side placement,
seat claim, separate Chinese/English replacement, draw-offer synchronization,
new-game clearing, and non-autoplay 32-visit AI Hint. Its final telemetry
included `ownershipReadbacks=1` for the root ownership output and
`finalMoveCount=0`.

Exact marker output and the limitation that independent-client opponent
acceptance is still open are recorded in
`Benchmarks/KaTrain/udon-clientsim-undo-draw-ui-2026-09-01.md`.

After these changes the final generator was executed twice in the isolated
Unity project. Both runs produced one generated root, 361 board cells, 765
UdonBehaviours, and `programs=42 compiled=42 compilerError=False`. The updated
Production Validator also passed with `failures=0 warnings=1`; the one warning
is only the known raw-oracle-outside-project-root warning.

## 2026-08-31 — Fresh-leaf Udon state guard and timing diagnostics checkpoint

`IMPLEMENTED` and `STATICALLY VERIFIED` for the current production-source
checkpoint. After a successful GPU neural readback, `GoAiController` now
releases the encoder/readback resources before the next PUCT leaf. This makes
the next leaf require a fresh feature encode instead of reusing the previous
leaf's completed encoder state. `GoFeatureEncoder.encodeGeneration` and the
stage timing counters in the Udon search/neural classes are diagnostic data;
they do not change the public difficulty presets or claim a performance result.

The updated sources were compiled by real Unity UdonSharp in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-perf-probe-compile.log`:

```text
PURE_UDON_GO_UDONSHARP_COMPILE_RESULT programs=41 compiled=41 missingSource=0 missingSerialized=0 compilerError=False
PURE_UDON_GO_UDONSHARP_COMPILE_PASS
```

The final production generator was rerun with the updated sources in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-final-state-fix-generate.log`:

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity root=PureUdonGo.ProductionRoot cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=763 programs=42 compiled=42 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

The updated validator is recorded in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-final-state-fix-validator.log`:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170e-b10c128-s1141046784-d204142634 maxVisits=1226
```

The warning is the known isolated-project warning that the raw KataGo oracle
is outside that Unity project root; the committed baked asset and manifests
were checked. The first fresh-feature ClientSim attempt is deliberately not
recorded as a pass: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-state-fix-clientsim-32b.log`
reached `searchVisits=23` before the bounded run timed out without advancing
the match. The first performance-probe attempt also stopped before PlayMode
because its temporary UdonSharp probe asset was stale;
`GoClientSimPerformanceVerifier` now retries compilation, but that retry was
not rerun in this stop-work checkpoint. Therefore this entry makes no timing,
4x speedup, or external strength-calibration claim.

## 2026-08-31 — Final production generator revalidation

`VERIFIED` for the normal Unity batchmode production path in the isolated
Unity 2022.3.22f1 project. After synchronizing the current checkout, the
following entry point was executed:

```text
Tools > Pure Udon Go > Generate Final Production Scene
```

The first complete run is recorded in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-final-generate-1c.log`:

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity root=PureUdonGo.ProductionRoot cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=763 programs=41 compiled=41 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

The same entry point was executed again in the same project. The second run
is recorded in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-final-generate-2.log`
and produced the same `generatedRoots=1`, `udonBehaviours=763`, and
`programs=41 compiled=41` counts, with no duplicate generated-root suffixes.
This is the generator idempotence evidence for the current checkout.

The standalone validator run is recorded in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-final-validator.log`:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170e-b10c128-s1141046784-d204142634 maxVisits=1226
```

The one warning only says that the raw KataGo oracle is outside the isolated
Unity project root; the committed baked asset and manifests were validated.
The runs used the explicitly imported copied Unity license and real D3D11
graphics device. This establishes Unity generation, serialization, UdonSharp
compilation, and structural validation; it does not claim VRChat client/device
runtime or external strength calibration.

The final repository-side regression rerun used the bundled workspace Python:

```text
Tests: 15 tests, 15 passed, 0 failed
Tools/KataGoModelConverter/tests: 12 tests, 12 passed, 0 failed
```

Both commands exited with code 0.

## 2026-08-31 — Real Udon KataGo V7 feature differential restored

`EQUIVALENCE VERIFIED` for the cooperative production encoder in the real
Unity UdonSharp + ClientSim path. Seven 19×19 fixtures were encoded by the
compiled Udon program and compared against the exact bundled KataGo oracle
(`g170e-b10c128-s1141046784-d204142634.bin.gz`) at an absolute tolerance of
`1e-6`. The corpus covers empty/opening/fight/ladder/capture/pass-history and
double-pass states; all `22*361*7 = 55,594` spatial values and `19*7 = 133`
global values passed with zero failures.

The clean Udon run is recorded in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-feature-clean.log`.
The clean differential report is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\FeatureOracle\2026-08-31-current\differential-clean.json`.
The authorized KataGo invocation and model/config provenance remain the ones
recorded in `Benchmarks/KaTrain/oracle-provenance-2026-08-31.md`.

The defect fixed in this checkpoint was a real Udon-only state-machine bug:
the two-liberty ladder branch failed to assign its current target location
before launching the async search, so it searched a stale/default point. The
fix is in `Assets/PureUdonGo/Runtime/GoFeatureEncoder.cs`; no editor-only
fallback or relaxed tolerance was used. The run also recorded three successful
ladder targets and two working moves for the capture fixture before the
diagnostic code was removed, then the clean rerun passed without diagnostics.

## 2026-08-31 — Real Unity D3D11 GPU Shader graph checkpoint

`EQUIVALENCE VERIFIED` for the committed production Shader graph on the real
Unity graphics device. The Editor-only verifier loaded the generated scene,
encoded the empty 19×19 production state, executed the actual
`PureUdonGo/NNLayer` Shader graph, read back all RenderTexture checkpoints, and
exported 17 tensors. The run used the NVIDIA GeForce RTX 3060 Laptop GPU and
completed 83 graph passes.

The clean Unity log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-verify-2.log`.
The GPU export is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoRulesCompile\Assets\PureUdonGo\Generated\shader-actual.pugnn`.
The CPU reference export was generated from the raw-versus-baked exact tensor
map and is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNReference\production-reference.pugnn`.
The differential report is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNShader\differential-2.json`.

At `atol=1e-4`, `rtol=1e-5`, all 17 records passed with zero mismatches. The
largest absolute error was `1.9073486328125e-5` at `rconv10`; policy,
value, score, and ownership outputs also passed. This is direct GPU numerical
evidence for the tested empty-board graph, not a claim that every possible
input state or platform has been validated. The full multi-position shader
corpus and platform/device runtime remain open gates.

## 2026-08-31 — Real Unity GPU Shader multi-position corpus

`EQUIVALENCE VERIFIED` for the same seven-state feature corpus already
exported by real Udon ClientSim. The Editor verifier now feeds each Udon
feature state into the actual generated `PureUdonGo/NNLayer` graph on the
NVIDIA GeForce RTX 3060 Laptop GPU and exports all 17 graph records per state:
119 records total, with zero differential failures at `atol=1e-4` and
`rtol=1e-5`.

The Unity log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-corpus.log`.
The CPU reference corpus is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNReference\corpus` and the Unity
exports are under
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoRulesCompile\Assets\PureUdonGo\Generated\NNCorpus`.
The seven per-case differential reports are under
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNShader\corpus`.

Every case passed. The largest observed error was
`3.5762786865234375e-5` in `capture-9-tromp/rconv10`; this remains below the
documented tolerance and no output record had a mismatch. The exact case
maxima and limitations are recorded in
`Benchmarks/KaTrain/udon-gpu-shader-corpus-2026-08-31.md`.

## 2026-08-31 — GPU float4 graph optimization A/B

`VERIFIED` for a correctness-preserving GPU graph optimization on the real
Unity D3D11 device. Convolution, aligned BatchNorm, and aligned vector-matrix
multiply now reuse packed RGBA reads across four output lanes; non-aligned
weight bases keep the scalar path. The 7-case/119-record numerical corpus
still passed with maximum absolute error `3.5762786865234375e-5`, and the
actual Udon 32-visit runtime smoke path still committed a legal `D16` move with
five completed readback stages.

The A/B timing logs are
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-corpus-baseline-timing-2.log` and
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-corpus-optimized-timing-2.log`.
The final numerical log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-corpus-optimized-final.log`.
Warm-run graph totals were `1224.562 ms` baseline and `1033.054 ms`
optimized; excluding the first case, which includes process/shader warm-up,
the measured difference was `826.950 ms` versus `803.542 ms` (`2.83%`). This
is graph-only host evidence, not a claim about VRChat or Quest latency. The
full data and confound caveat are in
`Benchmarks/KaTrain/udon-gpu-shader-optimization-2026-08-31.md`.

## 2026-08-31 — Real Udon PUCT versus policy proof

`VERIFIED` for genuine frame-stepped PUCT behavior in the serialized Udon
runtime. Four fixed opening/capture/fight/mixed positions were executed in
ClientSim with the actual feature encoder, GPU graph, five-stage readback, and
32-visit tree search. Each sample created 32 tree nodes and 351 legal root
edges, with 31 positively visited root edges and root visit sum 31. The final
selection differed from the root policy-prior argmax in three of four samples:
capture `Q16` vs policy `D4`, fight `D16` vs policy `Q4`, and mixed `C4` vs
policy `Q16`; opening selected `Q16` and its policy prior was also `Q16`.

The clean log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-quality-puct-3.log`.
The generated Udon verifier required all four samples to complete with
`readerCompletedStages=5`, `actualVisits=32`, one AI move each, and no
compiler/Shader/runtime failure marker. This proves that the selected move is
not unconditionally the raw policy argmax and that the real Udon tree expands
multiple root children. It is not yet proof of a monotonic strength ladder or
that backed-up values dominate root choice at higher visits; that requires a
controlled higher-visit PUCT sample and authorized KaTrain comparisons.

## 2026-08-31 — Real Udon UI, hint, and rules-boundary checkpoint

`VERIFIED` in the disposable real Unity/UdonSharp/ClientSim project. The
current generated control wall was rendered from the production scene in both
single-language modes. Its Udon button path passed PvP-only side placement,
language replacement, and a real 32-visit GPU/PUCT AI Hint that published
`D4` while leaving `moveCount=0` and `aiMoves=0`. The mode regression passed
the four controller combinations, independent profile events, and panel yaw
policy: default PvAI `0`, PvP `90`, AIvP/AIvAI `180`.

The expanded Udon rules probe passed ordinary capture, double-pass, singleton
suicide, positional ko, simple-ko fallback when positional superko is off,
search-state ko, three-stone suicide prisoner bookkeeping, pure rules API
separation, capacity terminalization, handicap, and resignation. The exact
markers, logs, and screenshot paths are recorded in
`Benchmarks/KaTrain/udon-client-sim-ui-rules-2026-08-31.md`.

## 2026-08-31 — Evidence reset: pre-ClientSim C# reports archived

`DEPRECATED_EDITOR_CSHARP`

The earlier Unity batchmode reports that drove the product through ordinary
Editor C# components and temporary Udon/VRChat API stubs have been moved to
`Benchmarks/DeprecatedEditorCSharp/`. They are retained for historical
debugging only and must not be cited as UdonSharp, ClientSim, VRChat, or final
world runtime evidence. This includes the old feature-oracle fixtures,
AI/search/AIvAI runs, generated-scene reports, and KaTrain move-quality
corpora. `Benchmarks/KaTrain/` is intentionally reserved for the replacement
evidence produced by an actual UdonSharp + ClientSim run.

The current authoritative runtime status is therefore: real UdonSharp
transpilation, serialized-Udon program generation, serialized-Udon execution,
ClientSim interaction, and one complete Udon-driven GPU-shader search turn are
verified in the isolated real SDK project described below. Full UX coverage,
public-preset strength calibration, and VRChat/device runtime remain open
gates. Model parsing/baking and shader-level
diagnostics remain valid only within their explicitly stated scope below.

## 2026-08-31 — Real UdonSharp + ClientSim GPU AI turn

`VERIFIED` for one complete default Beginner PvAI AI turn in the real Unity
SDK/ClientSim path. This is the first runtime evidence in this repository that
must be cited as Udon evidence; the archived C# reports remain deprecated.

The isolated project was regenerated and compiled with the real UdonSharp
compiler, then the following editor-side verifier started ClientSim PlayMode.
The verifier only dispatched Udon custom events and read serialized
`VRC.Udon.UdonBehaviour` variables; it did not call the Go runtime methods
directly from C#:

```text
Unity.exe -batchmode -projectPath E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoUdonClientSimTest -executeMethod PureUdonGo.GoClientSimRuntimeVerifier.VerifyClientSimRuntime
```

Evidence log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-12.log`

```text
PURE_UDON_GO_CLIENTSIM_PLAYMODE_ENTERED
PURE_UDON_GO_CLIENTSIM_UDON_EVENT events=SetWhiteStarts,RequestStartMatch baselineRevision=0 baselineMoveCount=0
PURE_UDON_GO_CLIENTSIM_STATE matchStarted=True revision=3 moveCount=1 aiMoves=1 searchVisits=32 controllerState=0 searchPhase=4 readerState=2 readerStage=-1 readerCompletedStages=5 readerError= runtimeError=
PURE_UDON_GO_CLIENTSIM_PASS matchStarted=True revision=3 moveCount=1 aiMoves=1 searchVisits=32 controllerState=0
PURE_UDON_GO_CLIENTSIM_RESULT pass=True
```

The same checkpoint was regenerated/compiled again in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-23.log`:

```text
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=762 programs=38 compiled=38 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

The updated validator passed in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-validator-2.log`:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=0 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170-b10c128-s1141046784-d204142634 maxVisits=1226
```

This establishes actual UdonSharp execution, VRC async GPU readback through
all five policy/pass/value/score/ownership stages, 32 completed PUCT visits,
and one AI move committed through `GoGame`. It does not establish numerical
KataGo equivalence, preset strength calibration, complete games,
networked multi-client behavior, or VRChat/device runtime.

## 2026-08-31 — Real Udon ClientSim mode and difficulty regression

`VERIFIED` for the generated world's user-facing control path. This regression
ran the actual serialized Udon programs in ClientSim and used the generated UI
events (`Toggle*Controller`, `Preset*`, `Select*Settings`, `ForceReset`, and
starter/start events), rather than invoking Go runtime methods from C#.

Evidence log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-modes-1.log`

```text
PURE_UDON_GO_CLIENTSIM_MODES_INITIAL blackIsAI=False whiteIsAI=True starter=1 blackVisits=32 whiteVisits=32
PURE_UDON_GO_CLIENTSIM_MODES_STATE mode=PvP
PURE_UDON_GO_CLIENTSIM_MODES_STATE mode=AIvP
PURE_UDON_GO_CLIENTSIM_MODES_STATE mode=AIvAI
PURE_UDON_GO_CLIENTSIM_MODES_PROFILE side=BLACK visits=64
PURE_UDON_GO_CLIENTSIM_MODES_PROFILE side=WHITE visits=128
PURE_UDON_GO_CLIENTSIM_MODES_PROFILE side=BLACK visits=514
PURE_UDON_GO_CLIENTSIM_MODES_STARTER white=True
PURE_UDON_GO_CLIENTSIM_MODES_STARTER black=True
PURE_UDON_GO_CLIENTSIM_MODES_PASS modes=PvP,PvAI,AIvP,AIvAI starter=BLACK blackVisits=32 whiteVisits=32 moveCount=1 aiMoves=1 searchVisits=32 readerCompletedStages=5
PURE_UDON_GO_CLIENTSIM_MODES_RESULT pass=True
```

This proves the default PvAI configuration, all four controller combinations,
independent black/white profile selection, the public `32/64/128/514` ladder
through the UI route, both starter events, and an actual AIvAI Udon GPU-search
move after restoring both sides to 32 visits. It does not prove that every
mode completes a full game, that networking works across multiple clients, or
that the visit ladder has been externally strength-calibrated.

## 2026-08-31 — Real Udon ClientSim stale-result cancellation

`VERIFIED` for reset-time invalidation of an in-flight GPU result in a real
ClientSim Udon run. The test started the default PvAI match through Udon events,
waited until the first GPU readback was genuinely pending, then invoked the
generated UI `ForceReset` event. It accepted PASS only when the old callback
was observed as stale and no move was committed.

Evidence log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-stale-1.log`

```text
PURE_UDON_GO_CLIENTSIM_STALE_PENDING revision=2 readerState=1 readerStage=0 searchPhase=1
PURE_UDON_GO_CLIENTSIM_STALE_EVENT name=ForceReset reason=cancel in-flight GPU result
PURE_UDON_GO_CLIENTSIM_STALE_PASS revisionBefore=2 revisionAfter=3 moveCount=0 aiMoves=0 staleCallbacks=1 searchPhase=4 readerState=4
PURE_UDON_GO_CLIENTSIM_STALE_RESULT pass=True
```

This proves a real pending Udon GPU request is cancelled on reset, the revision
changes, the reader records the late callback as stale, and the old AI result
cannot advance `GoGame`. Ownership transfer across multiple independent
clients still requires a separate networked runtime test.

## 2026-08-31 — Real Udon ClientSim pooled GPU resources and two-turn regression

`VERIFIED` for the current pooled `RenderTexture` lifetime across completed
Udon AI turns, with an explicit failed A/B captured before the fix. The first
pool attempt failed in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-15.log`:
the real Udon VM raised `IndexOutOfRangeException` while assigning
`blockOutputs[index]` at `GoGpuNeuralRuntime.cs:346`. The serialized Udon field
was a non-null zero-length array, so a null-only initialization check was not
valid. The fix now recreates the ten-entry array when its serialized length is
less than ten.

The fix regenerated the scene and passed the real UdonSharp compiler gate in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-35.log`:

```text
PURE_UDON_GO_GENERATOR_PASS ... cells=361 ... ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT ... generatedRoots=1 udonBehaviours=762 programs=39 compiled=39 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

The single-turn runtime rerun passed in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-16.log`:

```text
PURE_UDON_GO_CLIENTSIM_PASS ... aiMoves=1 searchVisits=32 lastMove=D16 searchBestMove=D16 rootEdges=362 rootPositive=2 rootVisitSum=31 rootMaxVisits=28 rootTop=Q16:3:0.102917,D16:28:0.103305
PURE_UDON_GO_CLIENTSIM_RESULT pass=True
```

The strengthened mode regression then required two completed AIvAI turns and
passed in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-modes-3.log`:

```text
PURE_UDON_GO_CLIENTSIM_MODES_PASS modes=PvP,PvAI,AIvP,AIvAI ... minAiMoves=2 moveCount=2 aiMoves=2 searchVisits=32 readerCompletedStages=5
PURE_UDON_GO_CLIENTSIM_MODES_RESULT pass=True
```

The pooled runtime also passed reset-time stale-result cancellation again in
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-stale-2.log`.
This is runtime evidence for safe reuse over two completed AI turns and the
existing cancellation path; it is not yet a latency/VRAM benchmark, full-game
evidence, multi-client ownership proof, or strength calibration.

## 2026-08-31 — Real Udon ClientSim difficulty search ladder

`VERIFIED` for the fact that all four public difficulty budgets execute as
real Udon GPU PUCT searches and reach their configured visit counts before a
move is committed. The benchmark used the generated UI/Udon profile events,
reset to an empty board between samples, and accepted each row only after a
new AI move plus five completed async output stages.

The complete table is committed at
`Benchmarks/KaTrain/udon-client-sim-ladder-2026-08-31.md`. The runtime log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-ladder-2.log`.
The measured rows were:

```text
Beginner  target=32  actual=32  elapsed=47.814s   frames=1094  nodes=32  edges=11524
Advanced  target=64  actual=64  elapsed=89.231s   frames=2619  nodes=64  edges=23046
Master    target=128 actual=128 elapsed=178.536s  frames=4809  nodes=128 edges=46087
UltraHard target=514 actual=514 elapsed=718.174s  frames=20060 nodes=514 edges=185054
PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=32,64,128,514
PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True
```

This verifies a monotonic search-effort/telemetry ladder, not a monotonic
playing-strength ladder. The single empty-board sample per preset is not
enough for Elo, oracle rank, or strength calibration; balanced position and
complete-game comparisons against the authorized KaTrain oracle remain open.

## 2026-08-31 — Real Udon ClientSim ownership transfer regression

`VERIFIED` for owner/lifecycle invalidation with a simulated remote player in
the real ClientSim environment. The verifier spawned a second ClientSim
player, started a real pending Udon GPU readback as local owner, transferred
the generated Go game object to the remote player, waited for cancellation,
handed ownership back, and required a fresh 32-visits AI move from the local
owner.

Evidence log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-owner-2.log`

```text
PURE_UDON_GO_CLIENTSIM_OWNER_PLAYERS count=2 localId=1 remoteId=2 initialOwnerId=1 isMaster=True
PURE_UDON_GO_CLIENTSIM_OWNER_PENDING revision=2 moveCount=0 aiMoves=0 ownerLifecycle=2 searchPhase=1 readerState=1 ownerId=1
PURE_UDON_GO_CLIENTSIM_OWNER_TRANSFER remoteOwnerId=2 ownerLifecycleBefore=2 ownerLifecycleAfter=4
PURE_UDON_GO_CLIENTSIM_OWNER_REMOTE_PASS ownerId=2 revision=2 ownerLifecycle=4 moveCount=0 aiMoves=0 staleCallbacks=0 cancelled=True searchPhase=4 readerState=4
PURE_UDON_GO_CLIENTSIM_OWNER_TRANSFER localOwnerId=1 ownerLifecycle=6
PURE_UDON_GO_CLIENTSIM_OWNER_PASS remoteOwnerId=2 localOwnerId=1 oldMoveCount=0 finalMoveCount=1 oldAiMoves=0 finalAiMoves=1 visits=32 readerCompletedStages=5 ownerLifecycle=6
PURE_UDON_GO_CLIENTSIM_OWNER_RESULT pass=True
```

This establishes the generated Udon authority check, owner lifecycle invalidation,
pending-readback cancellation, no old-owner move commit, and new-owner resume
path. ClientSim's cancellation implementation suppresses the late callback in
this case (`staleCallbacks=0`); the reset verifier separately observes a late
callback (`staleCallbacks=1`). This is not evidence from two independently
running VRChat clients, so real network transport and multi-client runtime
remain open.

## 2026-08-31 — Real Udon ClientSim complete AIvAI game

`VERIFIED` for a complete long game driven by the production Udon AI path. A
temporary ClientSim-only Udon probe configured both real AI sides to
`Custom=1 visit`, started AIvAI through `GoGame`, and waited for the actual
terminal state. The custom setting was only used to make a full 19×19 game
finite; the public 32/64/128/514 presets were not changed.

Evidence log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-complete-2.log`

```text
PURE_UDON_GO_CLIENTSIM_COMPLETE_RESULT passed=True moves=372 aiMoves=372 passMoves=18 terminalState=2 winner=-1 controllerState=0 failure=
PURE_UDON_GO_CLIENTSIM_COMPLETE_PASS
PURE_UDON_GO_CLIENTSIM_COMPLETE_RESULT pass=True
ERROR_MARKERS=0
```

The game produced 372 AI commits, reached a non-playing terminal state, and
recorded 18 pass moves including the required two-pass ending. The complete
report is committed at
`Benchmarks/KaTrain/udon-client-sim-complete-game-2026-08-31.md`. This proves
the long-game Udon state/search/readback/commit/terminal chain, but not public
preset strength, Elo, oracle rank, or balanced self-play calibration.

## 2026-08-31 — Real Udon ClientSim rules probe

`VERIFIED` for the production-rule set executed by a compiled UdonSharp probe
in ClientSim. The temporary probe called `GoGame`'s real Udon methods for a
capture sequence, a two-pass terminal sequence, a surrounded singleton suicide
attempt, a ko recapture, five-stone handicap placement, and resignation; the
editor harness did not mutate the board arrays.

Evidence log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-rules-5.log`

```text
PURE_UDON_GO_CLIENTSIM_RULES_RESULT capture=True doublePass=True suicide=True ko=True handicap=True resign=True captureMoves=3 terminalState=2 suicideMoves=8 koLocation=20 koMoveCount=13 handicapCount=5 resignState=4 failure=
PURE_UDON_GO_CLIENTSIM_RULES_PASS
PURE_UDON_GO_CLIENTSIM_RULES_RESULT pass=True
```

This establishes actual Udon execution for capture bookkeeping, double-pass
game termination, singleton suicide rejection, positional ko recapture
rejection, handicap placement, and resignation. Exhaustive legal-move matrices
and additional superko histories remain separate gates; a separate custom
one-visit AIvAI complete-game run is recorded below.

## 2026-08-31 — Real Udon position-quality smoke corpus vs KaTrain

`VERIFIED` for four independently executed Udon-vs-oracle position samples;
`NOT STRENGTH CALIBRATED`. A real ClientSim Udon probe generated the moves
`Q16`, `Q4`, `R16`, and `Q16` from opening, capture, fight, and mixed fixed
sequences. Each row completed 32 visits and all five async output stages. The
fixed authorized KataGo oracle ranked those Udon moves at zero-based orders
`0`, `2`, `9`, and `2`; absolute oracle winrate deltas were `0.000000`,
`0.001600`, `0.000065`, and `0.000054`, while score-lead deltas were
`0.000000`, `0.017912`, `0.025681`, and `0.179266`.

The complete sequences, oracle top groups, model/config hashes and caveats are
committed at
`Benchmarks/KaTrain/udon-vs-oracle-position-quality-2026-08-31.md`; the Udon
runtime log is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-quality-2.log`.
The oracle used fixed `searchRandSeed=20260831`, one analysis/search thread,
and `nnRandomize=false`. The production Udon model and KaTrain model differ,
and four positions at one preset are not evidence of Elo, monotonic strength,
or public difficulty calibration.

## 2026-08-31 — Public Udon ladder paired with fixed white-to-move oracle

`VERIFIED` for pairing the four real public Udon search samples with an
external fixed oracle on the same empty white-to-move board. Udon selected
`D16`, `D4`, `D16`, `D16` at 32, 64, 128, and 514 visits respectively; the
fixed KataGo oracle's corresponding D4/D16 entries were tied at zero-based
orders 16/17 with 56 visits each. The oracle's top group was the eight corner
points at 81 visits each.

The compact comparison is committed at
`Benchmarks/KaTrain/udon-ladder-vs-oracle-empty-white-2026-08-31.md` and the
query is
`Benchmarks/KaTrain/queries/udon-ladder-empty-white-2026-08-31.jsonl`. This
still does not establish public-preset strength ordering because the shipped
Udon g170 model and authorized KaTrain b10 model differ; balanced multi-game
calibration remains open.

## 2026-08-31 — Authorized KaTrain oracle re-established

`VERIFIED` for direct external-oracle execution only. The exact KaTrain root
was inspected and its configured engine/model were identified as KataGo
`v1.18.1`, Git revision `92ee95c0a4b25fec214da00951ab69e97e207729`, OpenCL on
the NVIDIA GeForce RTX 3060 Laptop GPU, with model
`b10c384h6nbttflrs.bin.gz` and `analysis_config.cfg`. Full hashes and the
reproducible query are recorded in
`Benchmarks/KaTrain/oracle-provenance-2026-08-31.md`.

The real 19×19 empty-board query used Chinese rules, komi 7.5, policy and
ownership output, and `maxVisits=32`. It returned a live policy/search
distribution with `rootInfo.visits=35`; top moves were the symmetric group
`R16/R4/C16/C4/D3/Q3/D17/Q17` at 10 visits each, followed by
`Q16/Q4/D16/D4` at 7 visits each. All tuning/cache writes for this run were
redirected to the allowed Temp `homeDataDir` override. This is an oracle
baseline, not yet Udon-vs-oracle move-quality or difficulty calibration.

## 2026-08-31 — First real Udon move-quality smoke comparison

`LOCAL / SYNTHETIC` and `VERIFIED` for one independently executed position;
not a strength calibration. The real ClientSim verifier now exports the Udon
move and root PUCT distribution. In
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-14.log`
it recorded Udon's first move as `D16` (`lastMoveLoc=60`) after 32 visits;
the root had 362 edges, 2 positive-visit children, visit sum 31, and
`Q16:3` / `D16:28` as the positive root children.

Against the fixed oracle query above, D16 was in the tied second group at
7 visits (order 10 in KataGo's zero-based list), with absolute differences of
`0.002693005` winrate and `0.02379138` score mean from the first listed move.
The full caveat, hashes, and raw-log paths are in
`Benchmarks/KaTrain/udon-vs-oracle-initial-32-2026-08-31.md`. The oracle model
(`b10c384h6nbttflrs`) differs from the shipped Udon model (`g170...`), so this
is only a smoke comparison; it is not evidence of model equivalence, Elo, or
the four-preset strength gradient.

## 2026-08-31 — Real SDK UdonSharp generation and structural validation

`VERIFIED` for the real Unity SDK compile/generator/validator gate; this is not
ClientSim or VRChat runtime evidence.

The production source was copied into the disposable project
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoUdonClientSimTest`, which
uses the parent project's real VRChat Worlds 3.7.6 packages and Unity
2022.3.22f1. The copied host license was accepted by Unity; the batchmode logs
record `Successfully resolved entitlements`.

Invocation:

```text
Unity.exe -batchmode -quit -projectPath E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoUdonClientSimTest -executeMethod PureUdonGo.GoUdonSharpCompileVerifier.GenerateAndVerifyRealUdonWorld
```

First clean run: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-6.log`

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity root=PureUdonGo.ProductionRoot cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=762 programs=38 compiled=38 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
Exiting batchmode successfully now!
```

The second execution, `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-7.log`, produced the same root/cell/program counts, no duplicate-name diagnostics, and the same world PASS marker. This is the current idempotence evidence for the generated scene in the isolated project.

The real Production Validator was then run against that generated scene. Its
log is `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-validator-1.log`
and it recorded:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=0 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170-b10c128-s1141046784-d204142634 maxVisits=1226
```

The generated report recorded `Status: PASS`, `Failures: 0`, and `Warnings: 0`.
These results prove real SDK UdonSharp compilation, automatic paired program
asset creation, repeatable scene generation, and structural production
validation. They do not prove that Udon programs execute in ClientSim, that
the GPU async readback completes in Udon, or that the integrated search has
been strength-calibrated.

## 2026-08-30 — Repository/reference inspection

### Target repository

`VERIFIED`

- Repository: `cocokoishi/udon-go-ai`.
- Default branch: `main`.
- Bundled KataGo source is present under `.KataGO/KataGo-master/`.
- Required raw production model is present at `.KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz`.
- GitHub repository metadata reports stored size 11,138,361 bytes and Git blob SHA `7dc3470c40f2d41b8bffdb5c772fe1698b080f79`.
- `AGENTS.md` now contains the absolute external-repository isolation rule: no GitHub repository except `cocokoishi/udon-go-ai` may be accessed or mutated.

### Bundled KataGo parser/source

`STATICALLY VERIFIED`

From this repository's bundled `.KataGO/KataGo-master/cpp/neuralnet/desc.cpp`:

- binary float blocks use `@BIN@` followed by little-endian FP32 values;
- non-finite values are rejected;
- convolution source order is `y,x,input-channel,output-channel`;
- BatchNorm source fields and merged-affine semantics are explicit;
- model version and cross-layer channel relationships are validated by KataGo.

### Project model parser

`VERIFIED` for synthetic regression; real production payload evidence is recorded below.

Committed implementation:

```text
Tools/KataGoModelConverter/katago_model_parser.py
Tools/KataGoModelConverter/tests/test_katago_model_parser.py
```

Local command:

```text
python -m unittest discover -s tests -v
```

Observed result on 2026-08-30:

```text
4 tests
4 passed
0 failed
exit code 0
```

The parser tests cover complete model consumption, trailing-data rejection, non-finite tensor rejection, and wrong-version rejection. The broader 11-test converter suite and the real-model parse are recorded below.

## 2026-08-30 — Production model parse and independent FP32 bake

### Real raw model

`VERIFIED`

The repository's only allowed raw model was parsed locally through the
committed parser:

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

### Production bake

`EQUIVALENCE VERIFIED`

- `Assets/PureUdonGo/Model/Generated/weights_rgba32f.exr` is a 1024×733,
  uncompressed float32 RGBA atlas with SHA-256
  `e00f701b6ef1498037e56a330f6243d2878ff9c74e6f6fddd5b0cf06a2f94309`.
- `Assets/PureUdonGo/Model/Generated/weights_rgba32f.raw` contains the exact
  3,002,368-float padded RGBA payload with SHA-256
  `10285a17fd7a47a3b6bdde08a26d13c34c9bbcde566dfbd66c5fa27911c65866`.
- `weights_rgba32f.asset` is the committed Unity-serialized RGBAFloat runtime
  copy; its SHA-256 is recorded in `ModelManifest.json`.
- `ModelManifest.json`, `packing_manifest.json`, `tensor_manifest.json`, and
  `tensor_reconstruction.json` record source identity, architecture/layout,
  dimensions, and artifact hashes, including the raw payload and runtime
  asset.
- The independent verifier reparses the raw model and directly compares all
  127 tensor byte ranges: 127 exact, 0 failures.
- A repeated bake produced identical hashes for the EXR and all manifests.
- Tampered EXR data and tampered manifest tensor hashes are rejected by the
  regression tests.

This proves raw-to-baked tensor reconstruction and the deterministic bytes
used to create the Unity runtime asset. It does not by itself prove shader
inference or runtime availability.

## 2026-08-30 — Unity production texture import

`VERIFIED`

An isolated Unity 2022.3.22f1 project under the allowed Temp root imported the
committed EXR with the new `GoModelTextureImporter` postprocessor. The real
batchmode log recorded:

```text
PURE_UDON_GO_MODEL_IMPORT_PASS format=RGBAFloat size=1024x733
```

The generated importer metadata was inspected directly and reported
`sRGBTexture=0`, `enableMipMap=0`, `filterMode=0` (Point),
`textureCompression=0` (Uncompressed), and
`DefaultTexturePlatform textureFormat=20` (RGBAFloat). A second import
produced the same report and meta SHA-256
`b9844977db69bc672907acda8b6ee44b0dc76ba79434c3f666fa1139ff4e2c4d`.

This is real Unity import/settings evidence for the EXR audit artifact. A
separate Unity readback found that this Unity EXR path does not preserve the
atlas lanes numerically (channel/Y interpretation and color conversion affect
negative values) despite those importer settings. Therefore the production
GPU runtime binds the committed `weights_rgba32f.asset`, whose raw RGBAFloat
payload was read back exactly; the EXR remains the independently decoded audit
artifact. The full UdonSharp project, async readback, generator, and PlayMode
remain unverified.

## 2026-08-30 — [ARCHIVED] Unity Go rules smoke validation (Editor C# / API stubs)

`DEPRECATED_EDITOR_CSHARP` for the current C# rules implementation in an isolated Unity
2022.3.22f1 project. The batchmode harness compiled and executed the actual
`GoGame` component with temporary Udon/VRChat API stubs and recorded:

```text
PURE_UDON_GO_RULE_PASS { failures: 0, result: PASS }
```

The corpus covered single-stone capture, multi-stone suicide, positional
superko/ko recapture, area and territory double-pass scoring with komi,
nine-stone handicap, resignation, board-domain preservation across 500 random
turns, and rejection non-mutation. This establishes only local Unity C# behavior;
UdonSharp transpilation, networking, feature differential equivalence and
VRChat runtime are still not verified.

## 2026-08-30 — [ARCHIVED] KataGo V7 feature encoder base layer (Editor C#)

`IMPLEMENTED` / `DEPRECATED_EDITOR_CSHARP` for the fixed 19×19 base fixture layer.

`Assets/PureUdonGo/Runtime/GoFeatureEncoder.cs` follows the bundled
`nninputs.cpp::fillRowV7` plane/global ordering and emits 22 spatial channels
and 19 global inputs in NCHW flat storage. It covers current/opponent stones,
1/2/3-liberty planes, ko/superko mask, five-ply history/pass flags, area
ownership, komi/ko/suicide/scoring/pass/parity globals, and a fixed-capacity
ladder search for planes 14–17.

The isolated Unity harness compiled the encoder and wrote three fixtures. A
separate Python comparison against the local reference transcription reported:

```text
FEATURE_FIXTURE_EQUIVALENCE_PASS cases=3 floats=7961
```

The fixtures cover empty board, perspective/history/ko, and pre-encore
territory mode. This was the initial local/source-derived checkpoint; the
executed KataGo differential checkpoint below supersedes it for sampled
production feature states.

### Target-repository CI

`TARGET-REPOSITORY CI BLOCKED`

Target workflow run:

```text
33296740663
```

Observed job state:

```text
job: inspect
conclusion: failure
steps: null
runner logs: unavailable
```

The job failed before a runner executed any workflow step. Therefore this is not evidence that parser tests or real-model parsing failed. It is also not a CI PASS. No other repository may be used as substitute evidence.

## 2026-08-30 — Temporary official KataGo oracle fallback

`VERIFIED` for local validation only; this is not a production dependency.

At the user's explicit request, the official `lightvector/KataGo` v1.18.1
Eigen AVX2 Windows release was downloaded into the allowed disposable Temp
area because the local source build is blocked by the missing Eigen3 CMake
package. The downloaded archive was checked against the GitHub release API
digest before extraction:

```text
asset=katago-v1.18.1-eigenavx2-windows-x64.zip
sha256=0d62ffa41ee04dd89dd1b80fe45e306c837231cb1e2dccb4f5780d0ca7c313db
path=E:\UnityPRJS\ShaderGPT_neoversion\Temp\KataGoRelease\v1.18.1
```

Observed from the executable:

- `KataGo v1.18.1`, Eigen(CPU), AVX2/FMA;
- bundled `runtests`: all rules/board/SGF/symmetry tests passed;
- bundled `runnnlayertests`: 7 configurations passed;
- bundled `runnnontinyboardtest` loaded the repository raw model and emitted
  policy/value/ownership output with exit code 0.

The executable and extracted files remain disposable Temp validation material;
the bundled source and repository raw model remain the production/reference
source of truth.

## 2026-08-30 — [ARCHIVED] GPU neural primitive execution (Editor C# driver)

`IMPLEMENTED` / `VERIFIED` for the low-level FP32 D3D11 primitives.

Committed production code:

```text
Assets/PureUdonGo/Shaders/PureUdonGoNNLayer.shader
Assets/PureUdonGo/Runtime/GoGpuLayerExecutor.cs
```

The isolated Unity 2022.3.22f1 project ran the shader with
`-force-d3d11` against real RenderTextures and recorded:

```text
PURE_UDON_GO_GPU_VECTOR_METRICS pool_max_error=0 matmul_max_error=0 vector_bias_max_error=0 vector_add_max_error=2.980232E-08
PURE_UDON_GO_GPU_PRIMITIVE_METRICS device=Direct3D11 conv_max_error=0 affine_max_error=0 add_max_error=2.980232E-08
PURE_UDON_GO_GPU_PASS failures=0
```

This verifies shader addressing/math for convolution, affine/ReLU, residual
add, global/value pooling, vector matmul, vector bias/ReLU, and spatial-vector
injection. It does not yet prove full production-network output equivalence,
async readback, UdonSharp transpilation, or VRChat runtime behavior.

The same graph was then driven through the real C# product path:
`GoGame.Start` → `GoFeatureEncoder.Encode` → preallocated 22-plane/19-global
FP32 upload textures → production serialized weights → 83 GPU passes. The
isolated Unity log recorded:

```text
PURE_UDON_GO_ENCODED_GPU_METRICS passes=83 policy=4.291534E-06 policy_pass=1.907349E-06 value=7.152557E-07 score=2.905726E-07 ownership=2.849847E-07 final_max=4.291534E-06
PURE_UDON_GO_ENCODED_GPU_PASS failures=0
```

This closes the local encoder-to-GPU-input path for the empty-board fixture;
async readback, randomized positions, UdonSharp transpilation, and VRChat
runtime remain open.

## 2026-08-30 — [ARCHIVED] Frame-stepped PUCT/MCTS and GPU integration (Editor C#)

`IMPLEMENTED` / `VERIFIED` for the fixed-capacity search core and its local
production-GPU integration.

Committed search components:

```text
Assets/PureUdonGo/Runtime/GoSearchState.cs
Assets/PureUdonGo/Runtime/GoMctsSearch.cs
Assets/PureUdonGo/Runtime/GoGpuNeuralOutputReader.cs
```

`GoSearchState` duplicates the production 19×19 move/capture/suicide,
positional-superko, pass/double-pass, scoring and resignation state without
network/UI side effects. `GoMctsSearch` stores a fixed-capacity tree, expands
legal placements plus pass from neural policy logits, selects with PUCT, and
backs up alternating-perspective win/loss/no-result value. `Step()` is a
frame-callable state machine: each leaf waits for an explicitly identified
neural result before continuing. Search lifecycle, game revision, settings
revision, owner lifecycle and four-lane position hash checks reject late
results.

The isolated Unity core harness passed:

```text
PURE_UDON_GO_MCTS_METRICS visits=8 nodes=8 edges=2524
root_move0=4 root_move1=3 best=0 root_value=0.4449512
PURE_UDON_GO_MCTS_STALE_METRICS discarded=1 token=7
PURE_UDON_GO_MCTS_PASS failures=0
```

The same isolated Unity D3D11 project then evaluated the actual serialized
KataGo production asset through the 83-pass GPU graph for the root and leaves,
fed the five outputs into PUCT, and passed:

```text
PURE_UDON_GO_GPU_MCTS_METRICS visits=3 nodes=3 edges=1084
best=288 root_mean_value=-0.003240308 stale=0
PURE_UDON_GO_GPU_MCTS_PASS failures=0
```

The production harness also checks the zero-history root snapshot and clamps
requested visits so a complete 19×19 policy expansion cannot exhaust the
fixed edge storage. These are local Unity C# / D3D11 execution results, not
yet UdonSharp-transpilation, asynchronous-readback, network-owner-transfer,
strength-gradient, self-play, or VRChat runtime evidence.

## 2026-08-30 — [ARCHIVED] Generated Go world product slice (Editor C#)

`IMPLEMENTED` / `VERIFIED` for editor-time production scene generation and
the first runtime product slice.

Committed product components now include:

```text
Assets/PureUdonGo/Runtime/GoDifficultyProfile.cs
Assets/PureUdonGo/Runtime/GoAiController.cs
Assets/PureUdonGo/Runtime/GoBoardView.cs
Assets/PureUdonGo/Runtime/GoBoardCell.cs
Assets/PureUdonGo/Runtime/GoUI.cs
Assets/PureUdonGo/Runtime/GoUiButton.cs
Assets/PureUdonGo/Runtime/GoTelemetry.cs
Assets/PureUdonGo/Runtime/GoGeneratedWorld.cs
Assets/PureUdonGo/Editor/GoWorldGenerator.cs
```

The fixed menu entry is now a real editor command:

```text
Tools > Pure Udon Go > Generate Final Production Scene
```

The generator creates/repairs a stable owned root, dark Surface/SurfaceRaised
environment, wooden 19×19 board, grid and star points, pre-created black/white
stone visuals, 361 lightweight interaction cells, last-move/ko/hover markers,
world-space Go controls, telemetry, camera/lights, game state, difficulty,
GPU executor/runtime/reader and PUCT references. Beginner/Advanced/Master/
UltraHard/Custom are real Go presets; their visit budget and CPUCT are consumed
by `GoAiController` rather than being display-only labels. The public ladder is
Beginner 32, Advanced 64, Master 128, and UltraHard 514 visits; Custom is
clamped to the current fixed tree's actual maximum of 1226 accepted visits. The initial
controller uses the verified production GPU path with a bounded Update state
machine and asynchronous final-output readback; network owner-transfer wiring
remains the next runtime milestone.

In the isolated Unity 2022.3.22f1 project, two executions of the same command
passed with identical structure. The later dedicated idempotence harness
measured:

```text
PURE_UDON_GO_IDEMPOTENCE_METRICS firstObjects=1281 secondObjects=1281 firstButtons=387 secondButtons=387 firstMaterials=27 secondMaterials=27
PURE_UDON_GO_IDEMPOTENCE_PASS failures=0
```

The generated scene was also rendered by its real Camera at 960×640 after
placing temporary sample stones; the render harness reported
`PURE_UDON_GO_GENERATOR_RENDER_RESULT PASS`. The image is disposable Temp
evidence, not a committed screenshot. PlayMode, UdonSharp transpilation,
VRChat/ClientSim interaction and final production validator are not yet
verified.

## 2026-08-30 — [ARCHIVED] Asynchronous final outputs and generated-scene AI loop (Editor C# / API stubs)

`IMPLEMENTED` / `VERIFIED` for the serialized asynchronous output state
machine and generated-scene controller path.

`GoGpuNeuralOutputReader` now requests policy spatial, policy pass, value,
score and ownership outputs one at a time. A VRChat SDK build uses the
`VRCAsyncGPUReadback` Udon event contract; the isolated non-SDK build uses
Unity `AsyncGPUReadback` only as a local validation fallback. A cancelled
request holds the next request until its callback is discarded, preventing an
old callback from being interpreted as a new leaf. The reader carries the
search token, node, game/settings revisions, owner lifecycle and four-lane
position hash into `TrySubmitToSearch`.

The real Unity D3D11 fallback readback completed all five stages in 58 frames
with synchronous comparison error 0 for policy/pass/value/score/ownership.
The same code compiled and ran through a Temp-only `VRC_SDK_VRCSDK3` API stub,
which exercised the production `VRCAsyncGPUReadback` branch: five stages in
77 frames, comparison error 0.

The generated scene was then loaded directly and its `GoAiController.Tick()`
loop was driven with the same VRC branch and committed production weight
asset. Beginner completed four PUCT visits and committed one legal white move:

```text
PURE_UDON_GO_GENERATED_AI_METRICS frames=200 ai_moves=1 move_count=2
white_stones=1 visits=4 chosen=60 reader_stages=5
PURE_UDON_GO_GENERATED_AI_PASS failures=0
```

The subsequent `GoNeuralTelemetryCheck` ran the same generated scene on the
real D3D11 device and verified that the five final outputs are consumed by the
runtime surface: decoded v8 score mean `17.61959`, score stdev `24.99817`, lead
`13.83429`, ownership mean `0.03540449`, and root output revision `5`. It also
caught and fixed the generated `panelText`-only telemetry binding path; the
panel now refreshes when either telemetry text target is present.

The VRC branch evidence is local API-stub execution, not a VRChat client or
UdonSharp transpilation result. VRChat owner transfer, network serialization,
PlayMode/ClientSim and higher-level strength/self-play validation remain open.

## 2026-08-30 — [ARCHIVED] Xiangqi-style Go scene rebuild (Editor C#)

`IMPLEMENTED` / `VERIFIED` in an isolated Unity 2022.3.22f1 project with the
host license consumed from the allowed Temp copy. This is production source
implementation plus real Unity editor execution; it is not yet UdonSharp
transpilation or VRChat-client evidence.

The old primitive/TextMesh-only scene builder was replaced by an idempotent
Go-specific builder that adapts the read-only Xiangqi reference's visual and
layout language:

- 60% world scale, 80% table-height scale, minimal undertray/legs/feet;
- walnut base, maple playing surface, brass trim, dark graphite environment;
- world-space UGUI/TMP control wall with Xiangqi-style shell, three screens,
  compact cards, blue-gray accent and spatial board audio;
- left Go match screen, center GPU/PUCT telemetry screen, right difficulty and
  rules screen;
- Go-only 19x19 grid, nine star points, A-T coordinates, black/white stone
  pool, 361 collider interactions and 361 UI intersection buttons;
- actual PvP/PvAI/AIvP/AIvAI actions, Go pass/resign/new-game actions, and
  synchronized profile controls named Beginner, Advanced, Master, UltraHard,
  Custom. The first scene prototype below displayed the pre-capacity 723-visit
  limit; the current production source uses the verified 1226-visit maximum;
- generated place/capture/pass/game-end/reset clips and a 2.2 m spatial
  AudioSource bound to the live Go game.

The isolated Unity logs recorded:

```text
generator-xiangqi-style-noto-1.log:
PURE_UDON_GO_GENERATOR_PASS ... cells=361 panel=3-screen ultraHardVisits=723
PURE_UDON_GO_GENERATOR_METRICS roots=1 cells=361 materials=27
PURE_UDON_GO_GENERATOR_PASS failures=0
generator-xiangqi-style-idempotence-1.log:
same PASS/metrics on the second generator execution
generator-xiangqi-style-structure-1.log:
PASS with 377 UGUI buttons, 377 persistent Udon listeners, profile checks,
one AudioSource and zero duplicate suffix objects
generator-xiangqi-style-render-1.log:
PURE_UDON_GO_GENERATOR_RENDER_RESULT PASS at 960x640
```

The rendered image is disposable Temp evidence at
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-scene.png`.
The local host has no `python` executable in PATH, so Python regression tests
were not rerun in this milestone; prior recorded Python evidence remains
unchanged. TMP Essentials are imported asynchronously on first clean-project
generation and the fixed menu resumes automatically after import.

## 2026-08-30 — [ARCHIVED] Go board spatial separation and stone seating correction (Editor C#)

`VERIFIED` in the same isolated Unity 2022.3.22f1/D3D11 project. Visual review
found that the thicker generated pebble mesh was still placed at the old
short-piece height. Its lower pole therefore intersected the maple surface.
The generator now uses a named stone center height (`0.232` in board-origin
coordinates), places the `-0.135` lower pole above the playing surface, and
raises the last-move/ko/hover markers above the stone crown.
`VerifyGeneratedScene` rejects a generated stone whose computed lower bound is
below the maple surface.

The control wall now has an explicit separated layout: its canvas uses
`PanelBackOffset=1.34` and `PanelLift=0.14`, while the validator computes the
actual world-space rear board edge with the board-origin scale and requires a
positive physical gap. The reference camera was pulled back and raised so the
complete 19x19 board and the Xiangqi-style three-screen Go control wall are
visible together.

Actual Unity evidence:

```text
go-stone-seating-v9-generator-1.log:
PURE_UDON_GO_GENERATOR_PASS ... cells=361 panel=3-screen ultraHardVisits=723
PURE_UDON_GO_GENERATOR_PASS failures=0
go-stone-seating-v9-render-1.log:
PURE_UDON_GO_GENERATOR_RENDER_PASS ... size=960x640
PURE_UDON_GO_GENERATOR_RENDER_RESULT PASS
go-stone-seating-v9-idempotence-2.log:
PURE_UDON_GO_GENERATOR_PASS failures=0
PURE_UDON_GO_GENERATOR_METRICS roots=1 cells=361 materials=27
```

The corresponding rendered image is disposable Temp evidence at
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-scene.png`.
Visual inspection of that D3D11 output shows the board in front of the wall,
the front board edge in frame, and the test stones seated on (not inside) the
maple surface. This remains Unity editor evidence; UdonSharp transpilation,
VRChat client behavior, and device runtime are not claimed.

## 2026-08-30 — [ARCHIVED] Xiangqi-parity Go controllers, exact visit ladder, and visual pass (Editor C#)

`IMPLEMENTED` / `VERIFIED` in the isolated Unity 2022.3.22f1 project. The Go
control layer now follows the latest read-only Pure Udon Xiangqi interaction
model without copying chess content:

- Black and White each have an independent synchronized `PLAYER`/`AI`
  controller, exclusive player id/name seat, and AI-AI control authority;
- join Black, join White, join the single human side, leave seat, toggle either
  controller, swap the human side, choose Black or White first, start match,
  force reset, and per-side profile target/apply actions are exposed in the
  world-space UI;
- PvP, PvAI, AIvP and AIvAI are derived from those side controllers;
  `mode`/`aiColor` remain compatibility mirrors rather than the source of truth;
- human placement/pass/resign and AI placement/pass paths are separate, so a
  human cannot override an AI turn while the authoritative owner can commit a
  verified search result;
- both Black and White AI profiles are generated and selected by side. The
  exact exposed ladder is Beginner 32, Advanced 64, Master 128, UltraHard
  514 visits, plus Custom clamped to the fixed tree;
- `GoMctsSearch` now reserves 444174 policy edges and 2048 nodes, giving an
  exact `MAX_SUPPORTED_VISITS=1226` capacity;
- grid line width is reduced to `0.0012` local units with line shadows/corner
  expansion disabled; stones use the rounded Unity sphere at scale
  `(0.436, 0.240, 0.436)` and center height `0.206`, leaving a measured safe
  gap above the maple surface. The walnut table and gallery lighting were
  brightened.

Actual Unity evidence:

```text
go-controller-rules-3.log:
PURE_UDON_GO_RULE_PASS { failures: 0, result: PASS }
go-match-controller-3.log:
PURE_UDON_GO_CONTROLLER_METRICS modes=4 starter=black+white seat_claims=2 ai_ai=1
PURE_UDON_GO_CONTROLLER_PASS failures=0
go-controller-mcts-1.log:
PURE_UDON_GO_MCTS_METRICS visits=8 nodes=8 edges=2524
PURE_UDON_GO_MCTS_PASS failures=0
go-controller-network-1.log:
PURE_UDON_GO_NETWORK_METRICS stale=1 owner_lifecycle=3 difficulty_revision=1 mode=0
PURE_UDON_GO_NETWORK_PASS failures=0
go-controller-ladder-generator-5.log:
PURE_UDON_GO_GENERATOR_PASS failures=0
go-scene-component-probe-3.log:
PURE_UDON_GO_COMPONENT_PROBE_VRC ... resolved=True
go-production-validator-edge-layer-2.log:
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=0 ... maxVisits=1226
go-generator-edge-layer-assertion-2.log:
PURE_UDON_GO_IDEMPOTENCE_METRICS firstObjects=1281 secondObjects=1281 firstButtons=387 secondButtons=387 firstMaterials=27 secondMaterials=27
PURE_UDON_GO_IDEMPOTENCE_PASS failures=0
go-board-edge-layer-render-1.log:
PURE_UDON_GO_GENERATOR_RENDER_RESULT PASS
go-stone-final-visual-d3d11-1.log:
PURE_UDON_GO_STONE_CLOSEUP_RESULT PASS
go-generated-ai-controller-9.log:
PURE_UDON_GO_GENERATED_AI_METRICS ... ai_moves=1 ... visits=40 ... reader_stages=5
PURE_UDON_GO_GENERATED_AI_PASS failures=0
```

The latest disposable D3D11 scene image is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-scene.png`;
the latest close-up is
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-stones-closeup.png`.
The final validator run used D3D11 because its RenderTexture checks must execute
with a graphics device. These are real editor/render and temporary API-stub
results, not UdonSharp transpilation, VRChat client, Quest, or calibrated
strength evidence.

## 2026-08-31 — [ARCHIVED] Single-layer board edge visual correction (Editor C#)

`VERIFIED` in the isolated Unity 2022.3.22f1 D3D11 project. The previous scene
had two perimeter systems: a separate `Trim · Front/Back/Left/Right` set at
±3.98 local units and the 19×19 grid's outer lines at ±3.78. Even though the
two sets used the same material and Y coordinate, the offset rendered as a
visibly thick/double edge. The generator now removes the duplicate Trim set and
uses the four outermost 19×19 grid lines as the only board ink edge, with a
`0.0012` local width and one `Y=0.086` coplanar layer. Coordinate labels follow
the grid edge. Both the generator assertion and reopened-scene validator fail
if a duplicate Trim object returns or if the outer lines drift from the grid
material/width/layer.

Actual Unity evidence after this correction:

```text
go-edge-single-layer-idempotence-1.log:
PURE_UDON_GO_IDEMPOTENCE_METRICS firstObjects=1277 secondObjects=1277 firstButtons=387 secondButtons=387 firstMaterials=27 secondMaterials=27
PURE_UDON_GO_IDEMPOTENCE_PASS failures=0
go-edge-single-layer-render-1.log:
PURE_UDON_GO_GENERATOR_RENDER_RESULT PASS
go-edge-single-layer-closeup-1.log:
PURE_UDON_GO_STONE_CLOSEUP_RESULT PASS
go-edge-single-layer-validator-1.log:
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=0 ... maxVisits=1226
```

The resulting disposable images are `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-scene.png` and
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-stones-closeup.png`.

## 2026-08-31 — [ARCHIVED] Decoded score head and no-result postprocess enter MCTS backup (Editor C#)

`VERIFIED` for the current local Unity implementation, with the exact scope
limited to the fixed-tree score path. `GoGpuNeuralOutputReader` now decodes the
KataGo V8 score mean, softplus score stdev, and no-result probability, then
derives unconditional score mean/stdev and lead for telemetry. `GoMctsSearch`
carries the same unconditional moments into a bounded score utility before
alternating backup. The utility uses a fixed 101-sample normal expectation at
KataGo's static scale `2` and dynamic scale `0.75`, with the current fixed
tree's zero dynamic center. This is closer to KataGo's expected-score shape
than the previous zero-uncertainty approximation, but it is not yet exact
KataGo parity: the production path still needs the reference score-value
table's quantization/interpolation and recent-score dynamic center, with a
direct numerical differential test.

Actual Unity evidence:

```text
go-score-postprocess-integration-1.log:
PURE_UDON_GO_SCORE_BACKUP_METRICS positive=0.2020932 negative=-0.2020933 mean=19 stdev=5 mixedMean=0.6713991 revision=1
PURE_UDON_GO_SCORE_BACKUP_PASS failures=0
go-gpu-mcts-score-postprocess-1.log:
Renderer: NVIDIA GeForce RTX 3060 Laptop GPU (ID=0x2560)
PURE_UDON_GO_GPU_MCTS_METRICS visits=3 nodes=3 edges=1084 best=301 root_mean_value=0.2936131 stale=0
PURE_UDON_GO_GPU_MCTS_PASS failures=0
go-neural-telemetry-score-postprocess-1.log:
PURE_UDON_GO_NEURAL_TELEMETRY_METRICS frames=3263 visits=40 score=17.61853 stdev=24.99779 lead=13.83345 ownership=0.03540449 revision=5
PURE_UDON_GO_NEURAL_TELEMETRY_PASS failures=0
```

The synthetic check proves score direction and no-result probability change
backed-up utility; the D3D11 checks prove the real serialized model's five-output
GPU path still reaches MCTS and telemetry. This remains local Unity/API-stub
evidence, not UdonSharp transpilation, VRChat runtime, exact score-head/table
equivalence, or strength calibration.

## 2026-08-31 — [ARCHIVED] Owner-authoritative lifecycle hardening (Editor C# / API stubs)

`VERIFIED` in the isolated Unity/API-stub project. `GoGame.Start` now only
initializes and serializes a revision when the local client is the authoritative
owner; a joining non-owner waits for the synchronized snapshot instead of
creating a local empty history. All Go state mutations now require ownership
acquisition to succeed, and a failed transfer returns before changing the
board, seats, settings, or match state. Deserialization and ownership changes
invalidate pending AI work before refreshing the view, while delayed recovery
hooks mirror the Xiangqi controller's join/leave/owner-recovery pattern.

Actual Unity evidence:

```text
go-network-authority-hardening-1.log:
PURE_UDON_GO_NETWORK_AUTHORITY_METRICS remoteRevision=0 ownerRevision=3 stalePhase=4 ownerLifecycle=0
PURE_UDON_GO_NETWORK_AUTHORITY_PASS failures=0
go-network-authority-match-controller-1.log:
PURE_UDON_GO_CONTROLLER_METRICS modes=4 starter=black+white seat_claims=2 ai_ai=1
PURE_UDON_GO_CONTROLLER_PASS failures=0
go-network-authority-stale-1.log:
PURE_UDON_GO_NETWORK_METRICS stale=1 owner_lifecycle=3 difficulty_revision=1 mode=0
PURE_UDON_GO_NETWORK_PASS failures=0
go-network-authority-generated-ai-1.log:
PURE_UDON_GO_GENERATED_AI_METRICS frames=3337 ai_moves=1 move_count=2 white_stones=1 visits=40 chosen=60 reader_stages=5
PURE_UDON_GO_GENERATED_AI_PASS failures=0
go-network-authority-generator-1.log:
PURE_UDON_GO_IDEMPOTENCE_METRICS firstObjects=1277 secondObjects=1277 firstButtons=387 secondButtons=387 firstMaterials=27 secondMaterials=27
PURE_UDON_GO_IDEMPOTENCE_PASS failures=0
```

This proves the authority gate and stale invalidation locally, not actual
multi-client VRChat ownership transfer or UdonSharp transpilation.

## 2026-08-31 — [ARCHIVED] First KaTrain position-quality record (Editor C#)

`DEPRECATED_EDITOR_CSHARP` for one historical integrated position, with status
`LOCAL_ORACLE_PARTIAL_NOT_STRENGTH_CALIBRATED`. The authorized KaTrain
installation was inspected and its exact KataGo oracle was established:
KataGo v1.18.1, revision `92ee95c0a4b25fec214da00951ab69e97e207729`, OpenCL
on an NVIDIA GeForce RTX 3060 Laptop GPU, model
`b10c384h6nbttflrs.bin.gz`. The executable was copied to the allowed Temp
oracle directory before tuning; no tuning/cache output was intentionally
written to the KaTrain tree.

The generated Unity scene then ran one 15-move fighting position with White to
move through the real D3D11 GPU graph at every exposed preset:

```text
Beginner   40/40 visits,   40 nodes,  13803 edges,   3417 frames, C10
Advanced  128/128 visits, 128 nodes,  44155 edges,  10906 frames, C10
Master    514/514 visits, 514 nodes, 177233 edges, 43418 frames, C10
UltraHard 1226/1226 visits, 1226 nodes, 422200 edges, 103482 frames, C10
```

The root neural-policy argmax was also `C10` for all four runs, so this
position is explicitly recorded as a PolicyOnly/PUCT-agreement baseline, not
as proof that MCTS changed the move. KaTrain analysis of the identical
position at 1000 visits ranked `D10` first and the integrated Udon move `C10`
second (zero-based order 1, top two). The recorded absolute score-mean delta
was `0.14397876` in the oracle's black-perspective output and the absolute
winrate delta was `0.002093184`; the forced `allowMoves` query also completed
1000 visits.

Evidence was committed under `Benchmarks/DeprecatedEditorCSharp/`, with the Unity run log at
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\go-difficulty-benchmark-policy-hook-1.log`.
One position cannot establish a monotonic difficulty gradient, MCTS advantage,
blunder rate, Elo/range, or complete-game win rate. Opening/fighting/tactical/
ko/endgame corpora and balanced complete games remain required.

## 2026-08-31 — [ARCHIVED] Four-position KaTrain corpus and real PUCT divergence (Editor C#)

`DEPRECATED_EDITOR_CSHARP` for 16 historical integrated position/preset runs and the corresponding
1000-visits KaTrain analysis. The Unity corpus covers `opening-7`, `fight-15`,
`ladder-19`, and `endgame-25`, each with Beginner 40, Advanced 128, Master
514, and UltraHard 1226 visits. All 16 cases completed their exact configured
visits in the real D3D11 GPU path; the raw Unity log and the persistent oracle
results are stored under `Benchmarks/DeprecatedEditorCSharp/`.

The profile-aware root selector now applies each preset's real temperature and
Top-K settings only to root edges with positive backed-up visits. With the
current score-postprocessed backup, the root policy argmax and final selected move
differed in 8/16 cases: one on `endgame-25` (Beginner), two on `ladder-19`,
two on `opening-7`, and one on `fight-15`.
All 16 selected moves were present in the oracle response, had positive Udon
root visits, and received a rank/top-k and score/winrate delta.

The current small-corpus aggregate is deliberately not a strength claim:

```text
preset     cases  mean oracle rank (0-based)  mean top-k  mean abs score delta  mean abs winrate delta  policy/PUCT divergences
Beginner      4              4.00                 5.00             0.320091                0.024698                  4
Advanced      4              1.75                 2.75             0.141308                0.011673                  2
Master        4              2.25                 3.25             0.185964                0.010050                  1
UltraHard     4              2.25                 3.25             0.185964                0.010050                  1
```

The corpus oracle is now run with KataGo's `forDeterministicTesting=true`,
`nnRandomize=false`, `numAnalysisThreads=1`, and `numSearchThreads=1` overrides.
Two independent processes were compared field-by-field across all 16 cases;
`katrain-corpus-score-postprocess-2026-08-31-reproducibility.json` records `PASS`
with zero differences. This establishes reproducibility of the current
score-postprocessed profile-aware position-level sample, not playing strength.

This currently does not demonstrate a monotonic oracle-quality gradient;
indeed the stronger presets are not measurably better in this four-position
sample. That is an unresolved calibration/search-oracle mismatch, not a pass.
At least 20 balanced complete adjacent-preset games plus a larger tactical/
ko/endgame position corpus remain required before any `STRENGTH CALIBRATED`
label.

## 2026-08-31 — [ARCHIVED] Requested 32/64/128/514 difficulty ladder verification (Editor C#)

`DEPRECATED_EDITOR_CSHARP` for the exact public preset values and local D3D11 execution. The
direct profile harness observed:

```text
Beginner  visits=32  nnQueries=32  transitions=1  CPUCT=1.85  temperature=1.15  topK=24
Advanced  visits=64  nnQueries=64  transitions=2  CPUCT=1.60  temperature=0.85  topK=64
Master    visits=128 nnQueries=128 transitions=4  CPUCT=1.35  temperature=0.30  topK=128
UltraHard visits=514 nnQueries=514 transitions=8  CPUCT=1.25  temperature=0.10  topK=362
PURE_UDON_GO_DIFFICULTY_PROFILE_PASS failures=0 presets=4
```

The regenerated scene passed Production Validator and the real asynchronous
D3D11 four-preset benchmark. All 16 cases in `opening-7`, `fight-15`,
`ladder-19`, and `endgame-25` completed their exact configured visits:

```text
Beginner 32/32   Advanced 64/64   Master 128/128   UltraHard 514/514
PURE_UDON_GO_CORPUS_PASS failures=0 cases=16
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=0
PURE_UDON_GO_IDEMPOTENCE_PASS failures=0
```

The current canonical external comparison uses the exact bundled production
model `g170e-b10c128-s1141046784-d204142634.bin.gz` (SHA-256
`1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04`) with the
authorized KataGo v1.18.1/OpenCL release from KaTrain. The four-position,
1000-visit oracle sample using production-matching Tromp-Taylor rules measured:

```text
preset     cases  mean oracle rank (0-based)  mean top-k  mean abs score delta  mean abs winrate delta
Beginner      4              1.50                 2.50             0.221822                0.007374
Advanced      4              1.50                 2.50             0.303807                0.008224
Master        4              1.25                 2.25             0.339610                0.012154
UltraHard     4              1.00                 2.00             0.253672                0.009817
```

Two independent matched-model oracle processes produced
`PURE_UDON_GO_KATA_REPRODUCIBILITY_PASS cases=16 differences=0`. The rank
trend is directionally better at UltraHard, but Advanced is slightly worse than
Beginner and score/winrate deltas are not monotonic. Therefore the arithmetic
and integration are verified, while this sample is explicitly
`LOCAL_ORACLE_PARTIAL_NOT_STRENGTH_CALIBRATED`; balanced complete-game
calibration and a larger tactical corpus remain open. Beginner and Advanced
tie on mean rank in this sample, and the score/winrate deltas are not strictly
monotonic.

The same regenerated scene also completed the current 32-visit Beginner AIvAI
continuity run: 340 seeded legal moves followed by 92 alternating AI moves,
29 capture moves, 6 pass moves, and a genuine double-pass result with Black
winning. Every AI move completed 32 visits with positive selected root visits;
the compact result and full Unity log are in `Benchmarks/DeprecatedEditorCSharp/`.

## 2026-08-31 — [ARCHIVED] Real D3D11 AIvAI continuity probe (Editor C#)

`DEPRECATED_EDITOR_CSHARP` as historical partial editor evidence with status
`LOCAL_D3D11_AIVAI_PARTIAL_INCOMPLETE_GAME`. The generated production scene was
run in AI-vs-AI mode on a 340-move legal dense position. Both sides then used
the real GPU/readback/PUCT controller for 100 alternating Beginner moves at 40
visits each. The selected move had positive root visits on every move; the run
recorded 37 captures and one genuine AI `PASS`.

The bounded run ended with `incomplete-game` rather than a fabricated success:
the opposing AI continued after the single pass, so double-pass completion was
not observed. The exact Unity log and compact summary are stored under
`Benchmarks/DeprecatedEditorCSharp/aivai-dense340-beginner-2026-08-31.*`. This verifies
multi-turn AIvAI continuity and pass handling in the generated D3D11 scene, but
not a complete-game result, strength calibration, or a monotonic preset ladder.

## 2026-08-31 — [ARCHIVED] Executed KataGo V7 feature differential and rule-boundary fix (Editor C#)

`DEPRECATED_EDITOR_CSHARP` for the executed sampled corpus. The production
`GoFeatureEncoder` was regenerated by Unity from the current `GoGame` rules,
then compared against input tensors dumped by the authorized KataGo v1.18.1
`evalsgf` executable using the repository raw model
`g170e-b10c128-s1141046784-d204142634.bin.gz` and the matching Tromp-Taylor
rules. The corpus contains eight cases: empty, opening, fight, ladder,
capture/singleton-suicide boundary, one-pass history, double-pass terminal
history, and a Chinese-rules control.

The exact executed result was:

```text
PURE_UDON_GO_FEATURE_ORACLE_DUMP_PASS cases=8
PURE_UDON_GO_KATAGO_FEATURE_ORACLE_PASS cases=8
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=8 failures=0 tolerance=1e-06
```

This compares all 7,961 spatial/global floats per case, including ladder,
superko-mask, history, terminal, and rules-global channels. Durable compact
evidence and oracle provenance are stored at:

```text
Benchmarks/DeprecatedEditorCSharp/katago_feature_oracle_differential-2026-08-31.json
Benchmarks/DeprecatedEditorCSharp/katago_feature_oracle_manifest-2026-08-31.json
```

The mismatch that preceded the pass exposed two real production bugs. KataGo's
`multiStoneSuicideLegal` does not legalize singleton suicide, and its
`superKoBanned` plane excludes ordinary illegal moves and all moves after game
end. KataGo also retains exactly the latest pass in a finished position. The
production `GoGame`, MCTS `GoSearchState`, and terminal history encoder now
follow those semantics. A separate real Unity regression passed with
`PURE_UDON_GO_RULES_SUPERKO_PASS failures=0`, including side-effect-free
singleton-suicide rejection and zero terminal superko-mask actions.

The eight-case differential is not an exhaustive randomized proof of every
KataGo rules/history variant; randomized and full-game expansion remain open.

## 2026-08-31 — [ARCHIVED] Randomized KataGo V7 feature differential expansion (Editor C#)

`DEPRECATED_EDITOR_CSHARP` for the expanded executed corpus. The Unity fixture
dump now stores each generated legal move sequence in the fixture itself, and
the oracle runner replays those exact sequences instead of maintaining a
second hard-coded move list. In addition to the eight fixed boundary cases,
the corpus contains eight deterministic random positions: four production
Tromp-Taylor histories (including pass insertion) and four Chinese-rule
controls, at 32/64/96/144 plies.

The first rerun exposed one real channel-17 ladder working-move mismatch. A
source comparison with bundled `board.cpp` showed that the production ladder
search was not clearing the root simple-ko assumption for the defender-first
two-liberty search and was not rejecting recursive simple-ko recaptures. Both
semantics are now implemented in `GoFeatureEncoder`, and the corrected run
passed:

```text
PURE_UDON_GO_FEATURE_ORACLE_DUMP_PASS cases=16
PURE_UDON_GO_KATAGO_FEATURE_ORACLE_PASS cases=16
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=16 failures=0 tolerance=1e-06
```

The durable expanded report and provenance are:

```text
Benchmarks/DeprecatedEditorCSharp/katago_feature_oracle_differential-randomized-2026-08-31.json
Benchmarks/DeprecatedEditorCSharp/katago_feature_oracle_manifest-randomized-2026-08-31.json
```

This is stronger sampled feature equivalence than the initial eight-case
checkpoint, but it remains a finite corpus rather than exhaustive randomized
coverage of every ko/handicap/long-history state.

## 2026-08-31 — [ARCHIVED] Corrected-rule complete D3D11 AIvAI game (Editor C#)

`DEPRECATED_EDITOR_CSHARP` as historical complete-game editor evidence with status
`LOCAL_D3D11_COMPLETE_GAME_NOT_STRENGTH_CALIBRATED`. After the feature/rules
correction above, the generated production scene ran a 340-move legal dense
position through 124 alternating Beginner AI turns. Every turn used exactly 32
PUCT visits, had positive selected root visits, and emitted finite score and
ownership telemetry. The game recorded 39 capture moves and 14 pass moves,
then ended by a genuine double pass at total move 464:

```text
PURE_UDON_GO_AIVAI_RESULT state=1 winner=1 moves=464 blackArea=259 whiteArea=53 score=-198.5
PURE_UDON_GO_AIVAI_PASS failures=0
```

The exact Unity log and compact summary are stored at:

```text
Benchmarks/DeprecatedEditorCSharp/aivai-dense340-beginner-32visits-2026-08-31-fixed-superko.log
Benchmarks/DeprecatedEditorCSharp/aivai-dense340-beginner-32visits-2026-08-31-fixed-superko-summary.json
```

This proves corrected-rule AIvAI continuity and a complete terminal path on
the real D3D11 generated scene. It is not a strength, Elo, or multi-game
calibration claim.

### Xiangqi visual/product-family reference

Only the user-supplied local ZIP is an authorized reference input. Its remote GitHub repository is outside the project boundary and must not be accessed.

The final Go world must visually inherit the same-author product-family language: dark modern spatial treatment, Surface/SurfaceRaised hierarchy, blue-gray accent, control proportions, table presentation, AI control panel language, telemetry/status styling, audio feedback language, and generator-owned scene structure.

## Verification labels in force

- `VERIFIED` — directly tested/observed for the stated claim.
- `STATICALLY VERIFIED` — established by source/asset inspection only.
- `EQUIVALENCE VERIFIED` — reserved for passed differential/numerical tests.
- `LOCAL / SYNTHETIC` — passed only against generated test inputs, not the production raw model.
- `NOT RUNTIME VERIFIED` — Unity/VRChat/device execution has not occurred.
- `TARGET-REPOSITORY CI BLOCKED` — target repo workflow was created/run but no runner steps executed.

## Current product status

### Repository/source prerequisites

- Raw KataGo model: `VERIFIED` present.
- Bundled KataGo source oracle: `VERIFIED` present.
- Absolute GitHub repository isolation: `VERIFIED` committed.
- Model v8 parser: `IMPLEMENTED`; `LOCAL / SYNTHETIC` 4/4 PASS.
- Real raw-model full parse: `VERIFIED` locally — version 8, exact EOF, 127 tensors.

### Model bake

- Production texture baker: `IMPLEMENTED`, `VERIFIED` on the real raw model.
- Production textures/manifests: `IMPLEMENTED`, generated in the checkout.
- Tensor reconstruction: `EQUIVALENCE VERIFIED` — 127/127 exact, 0 failures.
- Unity TextureImporter: `VERIFIED` in isolated Unity 2022.3.22f1 — RGBAFloat,
  linear, no mipmaps/compression, Point, Clamp.

### Go runtime

- Rules engine: `IMPLEMENTED`; the previous Unity C# smoke corpus is
  `DEPRECATED_EDITOR_CSHARP` and is archived. The production UdonSharp rules
  path is `VERIFIED` in ClientSim for capture, double-pass termination,
  singleton-suicide rejection, positional-ko rejection, handicap placement,
  resignation, simple-ko fallback, search-state ko, multi-stone suicide
  prisoner bookkeeping, pure rules legality, and capacity terminalization;
  the 361-point legal-move matrix at eleven histories and the fixed 160-action
  Udon/reference trace are `EQUIVALENCE VERIFIED`; broader randomized and
  exhaustive state coverage remains open.
- Feature encoder: `IMPLEMENTED`; the previous 16-case KataGo differential is
  `DEPRECATED_EDITOR_CSHARP` and is archived. The real ClientSim encoder
  capture is now `EQUIVALENCE VERIFIED` against the authorized KataGo feature
  dump for sixteen states, including eight deterministic random histories, and
  the GPU-head comparison is `EQUIVALENCE VERIFIED` for four fixed roots;
  exhaustive and broader history/handicap coverage remains open.
- Differential rules/features: current Udon neural-head differential is
  recorded in `Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-2026-09-01.md`,
  and the fixed 160-action rules/history differential is recorded in
  `Benchmarks/KaTrain/udon-clientsim-rules-differential-2026-09-01.md`;
  exhaustive matrices and randomized feature/history coverage remain open.
- Long-game Udon path: `VERIFIED` in ClientSim for one 372-move Custom=1
  AIvAI game reaching terminal double-pass scoring; public-preset complete-game
  samples remain open.

### Neural runtime

- GPU shader runtime primitives: `IMPLEMENTED`; `VERIFIED` only as a real
  Unity D3D11 shader-level diagnostic and a real UdonSharp/ClientSim turn.
- Fixed production GPU network graph: `IMPLEMENTED`; `VERIFIED` only through
  the Editor C# diagnostic driver and real UdonSharp/ClientSim execution over
  the committed serialized weight asset.
- Encoder-to-GPU input upload: the previous `GoGame` + `GoFeatureEncoder` C#
  path is `DEPRECATED_EDITOR_CSHARP` and is archived.
- Unityless exact full-network shader reference: `IMPLEMENTED`; its raw-vs-baked
  tensor map is exact and it supplies the full-output fixture.
- Layer/full NN equivalence: `EQUIVALENCE VERIFIED` for the empty-board full
  fixture and the four real Udon-captured root inputs. The ClientSim raw-head
  comparison has global maximum absolute error `9.5367431640625e-06` with
  zero mismatches at `atol=1e-4`, `rtol=1e-5`; the post-rollback per-case
  report is in
  `Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-rollback-2026-09-02.json`.
- Unity async final-output readback: the previous D3D11 C# readback result is
  `DEPRECATED_EDITOR_CSHARP` and is archived.
- VRCAsyncGPUReadback branch: `VERIFIED` in the real UdonSharp/ClientSim
  project for all five policy/pass/value/score/ownership stages; the old Temp
  API-stub result is archived and is not used as evidence.
- Randomized full-input equivalence remains `NOT VERIFIED`; the four-case
  ClientSim capture is a fixed corpus, not randomized coverage.

### Search

- Fixed-capacity frame-stepped PUCT/MCTS: `IMPLEMENTED`; the Unity C# core
  regression and GPU integration results are `DEPRECATED_EDITOR_CSHARP` and
  are archived. Genuine Udon-driven search is `VERIFIED` for 32 visits in
  ClientSim, including nontrivial root edge/visit telemetry and two completed
  AIvAI turns.
- Score/ownership delivery, async GPU-to-search, controller/seat/starter
  state, and local stale checks: the real Udon/ClientSim paths are `VERIFIED`
  for the current single-client scenarios; previous C# or API-stub results are
  `DEPRECATED_EDITOR_CSHARP` and are not used as evidence.
- Owner-authoritative networking and stale-result protection: `VERIFIED` in
  ClientSim for local/remote ownership transfer, lifecycle invalidation,
  pending-readback cancellation, and local-owner resume; two independently
  running VRChat clients and real network transport remain `NOT RUNTIME
  VERIFIED`.
- Search performance gradient: `VERIFIED` in real ClientSim for all four
  public budgets; measured 32/64/128/514 actual visits, increasing tree sizes,
  and per-sample elapsed time are committed in
  `Benchmarks/KaTrain/udon-client-sim-ladder-2026-08-31.md`. The 16-case
  same-model position comparison is committed in
  `Benchmarks/KaTrain/udon-clientsim-search-oracle-comparison-2026-09-01.md`.
  Its exact visit/candidate coverage is verified, but its non-monotonic mean
  oracle ranks keep playing-strength calibration `NOT ESTABLISHED`.
- Position-level oracle smoke comparison: `VERIFIED` for four real Udon
  ClientSim positions against the fixed authorized KataGo configuration;
  orders and score/winrate deltas are committed in
  `Benchmarks/KaTrain/udon-vs-oracle-position-quality-2026-08-31.md`. The
  differing production/oracle models and four-position sample do not establish
  strength calibration.

### Unity product

- Runtime board/view/UI/telemetry/difficulty/AI product slice:
  `IMPLEMENTED`; generated-world structure, real language-switched renders,
  PvP-only wall placement, generated-button interaction, and non-auto-play AI
  Hint are `VERIFIED` in ClientSim.
- `Tools/Pure Udon Go/Generate Final Production Scene`: generator source is
  `IMPLEMENTED`; the real SDK compile/generator result is `VERIFIED` with one
  generated root, 361 board points, 6498 scene UdonBehaviours, 47 generated
  programs compiled, and no compiler error. The current generator run and
  Production Validator are recorded in
  `Benchmarks/KaTrain/udon-clientsim-finalization-2026-09-02.md`; the validator
  emitted one expected raw-oracle-outside-temp-root warning.
- Generated scene visual render and Production Validator: previous D3D11
  editor diagnostics are `DEPRECATED_EDITOR_CSHARP`; the real generated-world
  validator and ClientSim runtime are `VERIFIED` for the covered paths.
- UdonSharp transpilation, PlayMode/ClientSim interaction and Unity runtime:
  `VERIFIED` for the isolated real SDK project and covered scenarios.
- VRChat runtime: `NOT RUNTIME VERIFIED`.
- Quest runtime: `NOT RUNTIME VERIFIED`.

## 2026-09-01 — Walkable generated floor and post-optimization runtime gates

`VERIFIED` for the real isolated Unity/ClientSim path. The generated gallery
floor now retains an enabled `PlaneCollider`; the corrected runtime log has
`RESPAWN_LINES=0`, one initial player placement, zero compiler/Shader/Udon
failure patterns, and a completed real 32-visit Udon AI turn. The old runtime
verifier's stale `visits+1` encoding assertion was corrected to the actual
one-fresh-encode-per-completed-visit invariant (`encodeGeneration=32`), and
the rerun passed.

The actual Unity `Button.onClick` UI probe was rerun on this generated scene:
PVP, seat claim, independent Chinese/English label replacement, draw offer,
new-game clearing, and non-autoplay GPU/PUCT Hint all passed. Evidence and
exact Temp logs are in
`Benchmarks/KaTrain/udon-clientsim-floor-ui-2026-09-01.md`. This remains
isolated ClientSim evidence, not physical VRChat/Quest runtime evidence.

## 2026-09-01 — State-driven Go controls and bounded UI refresh

`VERIFIED` in real UdonSharp/ClientSim. The generator now serializes the 29
control-wall Unity Buttons into Go's state-driven UI. `RefreshControlState`
sets their actual `Button.interactable` values from live mode/seat/turn/offer/
AI state; the ClientSim probe passed initial disabled controls, PvP seat
enabling, draw availability, undo locking, and human-turn Hint availability.
The same probe invoked persistent `Button.onClick` listeners and kept Hint
non-autoplay (`visits=32`, `finalMoveCount=0`).

The UI cache gate measured `refreshDelta=4` over 60 idle pump frames; active
search progress is sampled at 0.16 seconds. Generation and Production
Validator passed with the serialized arrays. Exact markers and the limitation
to isolated ClientSim are in
`Benchmarks/KaTrain/udon-clientsim-ui-state-2026-09-01.md`.

## Active development order

1. Expand real Udon/ClientSim rules and feature differential coverage beyond
   the fixed long-history, eleven-checkpoint legal matrix, and current 16-state
   randomized feature corpus, including broader superko/handicap states;
2. Add isolated multi-client ClientSim evidence for owner authority, ownership
   transfer, reconnect identity and stale-result rejection;
3. Keep the valid clean-host 32/64 performance baseline reproducible and
   record later 128/514 measurements only when the GPU environment is clean;
4. Record position-level Udon moves against the authorized KataGo/KaTrain
   oracle, then run balanced multi-game preset calibration before making any
   strength claim;
5. Complete physical VRChat/Quest inspection and rerun target-repository CI
   when runner execution exists.

## 2026-09-02 — Finalization slice: analysis UI and Udon regressions

`VERIFIED` for the current generated-world production slice in real Unity
2022.3.22f1 D3D11 ClientSim. The center analysis surface now uses compact
player-facing fields (`AI STATE`, search progress, `BEST MOVE`, three PUCT
candidate moves with visits/prior/Q, win probabilities, W−B score lead and
uncertainty, ownership mean, captures and last move). Old revision diagnostics
are no longer the primary text. Candidate data is read from the actual root
tree and published through the owner's telemetry snapshot; it is not a
policy-only or fabricated display.

The telemetry verifier now resolves the same `BoardPool.primaryUI` table as the
game and AI programs. The real UI run passed actual Unity `Button.onClick`
events, independent Chinese/English replacement, PvP-only side placement,
AI-containing rear-wall placement, AI Hint without auto-play, and the new
analysis text gate. A stale analysis snapshot is cleared whenever a Go
position changes; a published hint is explicitly allowed to retain the
unchanged board-position analysis for its one metadata revision.

The same current scene passed the 16-slot/3-visible pool regression, Undo/Draw
consent regression, PvP/PvAI/AIvP/AIvAI and 32/64/128/514 profile-selection
regression, and a 372-move real Udon AIvAI complete-game smoke. The stale-result
probe now covers both valid outcomes: an early cancellation with no committed
neural output and a later callback counted as stale. The consolidated markers
and exact logs are in
`Benchmarks/KaTrain/udon-clientsim-finalization-2026-09-02.md` and `.json`.

The follow-up capture gate is also `VERIFIED`: a real Udon rules probe observed
the captured stone during the 0.22-second board-level sink/shrink animation,
and the regenerated scene's Production Validator remained at zero failures.

This is not a claim of physical VRChat/Quest runtime, independent multi-client
transport, or balanced strength calibration. The public search ladder remains
an exact-budget/functional ladder until the documented oracle calibration gate
is completed.
