# Search semantics — exact optimization boundary

The implementation agent may change data structures and scheduling, but not the
meaning below.

## 1. Rules/history

Use current executable rule code and bundled KataGo/oracle comparisons as the
source of truth for:

- capture;
- ko and positional superko;
- pass/double-pass;
- scoring/komi;
- handicap/resign;
- current suicide behavior;
- exact accepted position history.

Caches and indexes are allowed only if they return the same legality/history
answer.

### Chinese compensation and score lifecycle

For area scoring, `GoGame.komiTimes2` remains the synchronized ordinary komi.
The authoritative effective white compensation is
`komiTimes2 + handicapStones * 2` in half-point units. `GoSearchState` and direct
`GoFeatureEncoder` game entry points consume that effective value exactly once.
Terminal `finalWhiteMinusBlackScore` is the committed board-area score; current
UI/telemetry previews never write the synchronized final fields. During play,
the displayed W−B score and Chinese equivalent use the current root NN lead,
with the equivalent value equal to lead × 0.5. Ownership presentation is a
separate background lifecycle and cannot gate root score readiness.

### Authoritative group optimization

Port the generation-stamp group/liberty algorithm already used by
`GoSearchState` into `GoGame`. This is a representation/scratch optimization,
not a rule change. Preserve neighbor order, group members, capture result, ko and
suicide semantics.

### Exact history bucket optimization

The stone-count frequency reject remains valid. Add a stone-count bucket index
in `GoSearchState` so a positive count walks only compatible history entries.
Each candidate still compares all four hash lanes exactly.

Simulation-appended bucket entries are pushed/popped with simulation history;
there is no approximate hash shortcut.

## 2. INPUTSVERSION 7

`GoFeatureEncoder` must preserve the existing 22 spatial / 19 global feature
semantics and the repository's established oracle tolerances.

The current feature verifier exports 16 deterministic cases including empty,
opening, fight, capture, pass history, Chinese/Tromp variants, random positions
and `ladder-19-tromp`. Keep that corpus and run the independent local oracle
comparison after any encoder change.

Do not:

- drop history planes;
- zero/approximate ladder planes;
- skip exact superko input;
- cap ladder work to improve timing;
- change previous-board semantics.

### Ladder delta journal

The planned ladder optimization changes only working-board restoration:

```text
copy source board once
record original value on first mutation of a point in an attempt
perform exact existing ladder logic
restore only touched points
```

Every ladder mutation must pass through one helper. Candidate generation,
attacker/defender sequence and terminal classification remain identical.

## 3. Neural inference

Keep the current generated model/layout and shader math within established
numerical tolerance.

Outputs remain:

```text
policy spatial
policy pass
win/loss/no-result
score mean/stdev/lead where used
ownership
```

One packed readback is the intended implementation and must remain one request
per evaluation.

Input-upload restructuring may remove copies but may not change the exact float
values received by the graph.

## 4. Resolved difficulty

A complete resolved profile is:

```text
maxVisits
maxNNQueries
maxTransitionsPerFrame
cpuct
moveTemperature
policyTopK
resignThreshold
```

Public presets are Beginner 8, Advanced 20, Master 48, and Ultrahard 120 visits.

Custom means visits-only override of the complete synchronized shared preset.
This rule is part of search semantics because it defines reproducible search
behavior.

## 5. Immutable SearchSession

`BeginSearch` captures the complete profile and root identity. The session keeps
those values until completion/cancellation.

A later settings Apply/deserialization changes only future `BeginSearch`.
A real position or owner-lifecycle change may invalidate the current session.

## 6. PUCT/MCTS

Keep actual PUCT selection, neural expansion, score/value utility, backup and
root move selection.

Do not change:

- requested visit target;
- PUCT formula/constants except via the resolved profile;
- legal candidate meaning;
- neural prior normalization;
- backup perspective/sign;
- root temperature semantics;
- resign semantics.

The current fixed-capacity/lazy tree architecture remains until profiling after
the higher-priority phases shows a measured reason to change it.

## 7. MCTS performance thresholds

After group/history/ladder/scheduler work and the fixed performance corpus:

- replace remaining root simulation copies only if
  `ResetSimulationToRoot` is at least 10% of measured visit CPU time;
- optimize linear child scanning only if selection scan is one of the top three
  remaining CPU costs;
- do not approximate the 101-sample score utility. Change it only with a
  dedicated numerical comparison to the bundled KataGo semantic target.

## 8. Search calibration

Neural equivalence does not prove search equivalence.

Use the current search calibration fixture/tooling and compare:

```text
root target/completed visits
candidate moves
candidate priors
child visit counts
root value/score
chosen move
```

Performance changes that cause unexplained search-distribution or chosen-move
changes fail until explained or reverted.

## 9. Forbidden performance shortcuts

Never claim improvement by:

- reducing visits/NN queries;
- changing the fixed A-H benchmark fixture after the before run;
- reducing policy candidates without semantic justification;
- dropping history/superko/ladder work;
- replacing MCTS with policy argmax;
- changing rules/model;
- increasing timeout instead of reducing actual work.

## 10. KataGo source audit checkpoint (2026-09-10)

The bundled `.KataGO/KataGo-master` tree was inspected read-only. The fixed
production rule tuple in `GoGame` (`positionalSuperko=true`, `areaScoring=true`,
`multiStoneSuicideLegal=true`, komi 7.5) matches KataGo's
`Rules::getTrompTaylorish()` defaults (`cpp/game/rules.cpp:85-95`). The current
V7 encoder maps the normal-phase 22 spatial and 19 global channels in the same
order as `NNInputs::fillRowV7` (`cpp/neuralnet/nninputs.cpp:2302-2744`): stone and
liberty planes, ko/superko, five-ply history, three ladder planes, area and
komi/rule/parity globals. Historical external oracle artifacts contain exact
8-case and randomized-16-case matches at tolerance `1e-6`; the post-09782ee
change only skips a redundant zero-buffer clear and does not change feature
values. A fresh external oracle run is still recorded as
`ORACLE_NOT_PERFORMED_CURRENT` for the present commit.

The raw model heads retain KataGo's player-to-move convention. `GoMctsSearch`
consumes the value/score in that convention; `GoAiController` converts score
and ownership to white-positive only for presentation. This is consistent with
KataGo's `nneval.cpp:1290-1450` and `:1468-1483` postprocessing boundary.

This is not a claim that the small Udon tree is bit-for-bit equivalent to the
full KataGo search implementation. The current `GoMctsSearch.SelectEdge`
(`Assets/PureUdonGo/Runtime/GoMctsSearch.cs:588-598`) uses the product's fixed
cpuct and signed edge averages, while KataGo's `searchexplorehelpers.cpp:9-53`
adds dynamic cpuct, utility-stdev scaling, FPU and other optional search terms.
Likewise, the Udon score utility (`Assets/PureUdonGo/Runtime/GoMctsSearch.cs:751-780`)
uses a fixed 101-point quadrature with a zero dynamic center; KataGo's
`searchhelpers.cpp:271-292` uses its recent score center and configurable utility
factors. Those are intentional product-search differences and require a
separate numerical/search-distribution gate if exact KataGo search parity is
required.

The production default remains the legacy Udon search mode (fixed profile cpuct,
zero-Q for unvisited children, zero dynamic score center and three-way
no-result softmax). This is intentional and preserves existing chosen-move
behaviour until an ablation has evidence to change it. A desktop diagnostic
reference is checked in at `Tools/KaTrain/udon_search_reference.py`; a real
ClientSim expansion tape currently matches its root visits, four-lane edge
values and deterministic root ranking exactly (`referenceMatch=true`).

The professional endgame comparator is now explicitly scoped to 4 and 32
visits. It reports two independent KataGo output orderings: raw root
`edgeVisits` argmax (with ties represented as a set) and KataGo's
`playSelectionValue` ordering. The latter is the source of `moveInfos[0]` in
`cpp/search/analysisdata.cpp`; it is not interchangeable with a visit-count
winner. See `KATAGO_ACTIVE_SEARCH_PATH.md` for the exact bundled engine path
and comparator definitions.

The checked-in 4/32 rows reclassify the old screen to 28/32 raw-visit-set
matches at 4 visits and 29/32 at 32 visits. The remaining raw mismatches are:

- 4 visits: `master-park-junghwan-f48`, `master-park-junghwan-t25`,
  `master-nie-weiping`, `master-gu-li`;
- 32 visits: `cho-chikun-2021-01-07`, `master-park-junghwan-t25`,
  `master-nie-weiping`.

These are comparator-corrected observations, not exact KataGo-tree parity. A
bounded Udon root-decision tape is now available for the next fresh ClientSim
run; no production FPU/cpuct/score change is enabled by this audit.

`GoMctsSearch.ConfigureSearchSemantics` exposes isolated, opt-in diagnostic
bits for KataGo-like FPU, dynamic cpuct, recent-score-center and rule-aware
no-result handling. These bits are not serialized as product settings and are
only enabled by the professional-endgame verifier through an environment
variable. The corresponding Unity tapes for each individual bit and the
desktop numerical vectors pass; no product ablation is called a KataGo parity
result until it is benchmarked against the same-budget oracle.

For the rule-aware bit, KataGo's `nneval.cpp:1368-1379` behaviour is reproduced
by removing the no-result logit before selecting the softmax maximum and then
renormalizing win/loss. This detail is covered by the vectors in
`Tools/KaTrain/tests/test_udon_search_reference.py`. The default three-way
softmax remains unchanged. Search fidelity and deep-reference strength are
reported separately; a low same-budget Top-1 match alone is not a
catastrophic-strength verdict.
