# KataGo active search path for the 4/32 parity audit

This note describes the exact bundled engine used by the professional-endgame
oracle. It is an audit record, not a claim that the Udon tree implements the
whole KataGo search.

## Oracle identity

- engine: KataGo v1.18.1, revision `92ee95c0a4b25fec214da00951ab69e97e207729`
- executable/config: the copy under `KaTrain/_internal/katrain/KataGo`
- model: the checked-in `g170e-b10c128-s1141046784-d204142634.bin.gz`
- rules: Tromp-Taylor, positional superko, area scoring, komi 7.5
- query overrides: `forDeterministicTesting=true`, `nnRandomize=false`,
  `numAnalysisThreads=1`, `numSearchThreads=1`
- parity scope: `maxVisits=4` and `maxVisits=32`; the historical 100-visit
  artifacts are not included in the focused comparison.

## Active search parameters

The analysis command loads `Setup::loadSingleParams(...SETUP_FOR_ANALYSIS...)`
from `cpp/program/setup.cpp`; the config file only overrides the values that
are uncommented. For the current deterministic query the relevant defaults are:

| feature | active value/path |
| --- | --- |
| root Dirichlet noise | disabled (`rootNoiseEnabled=false`) |
| NN randomization | disabled by query override |
| cpuct | `cpuctExploration=1.0`, `cpuctExplorationLog=0.45`, base `500` |
| exploration scaling | `cpuctExploration(totalChildWeight) * sqrt(totalChildWeight + 0.01)` (`searchexplorehelpers.cpp:9-34`) |
| FPU reduction | `fpuReductionMax=0.2`, root default `rootFpuReductionMax=0.1` |
| FPU base | parent utility, blended by visited policy when enabled in analysis setup (`searchexplorehelpers.cpp:265-320`) |
| utility stdev scaling | analysis default `cpuctUtilityStdevScale=0.85` |
| score utility | static factor `0.1`, dynamic factor `0.3`, dynamic center zero weight `0.2`, scale `0.75` |
| LCB | enabled by analysis defaults; `lcbStdevs=5`, minimum visit proportion `0.15` |
| root ending/pass behavior | `conservativePass=true`, ending/pass heuristics enabled by setup defaults |
| chosen-move temperature | analysis default `0.1` (the oracle report is separately compared to raw visits) |
| policy candidate handling | all legal root children are represented; no Udon-style product Top-K truncation is applied at expansion |

The exact effective values should be confirmed from a raw engine log whenever
the KaTrain installation changes. A commented line in `analysis_config.cfg` is
not an active setting.

## Output comparators

`cpp/search/analysisdata.cpp:181-194` sorts `moveInfos` by:

1. non-zero `numVisits` before zero visits;
2. descending `playSelectionValue`;
3. descending `numVisits`;
4. descending raw policy prior.

Therefore `moveInfos[0]` is **KataGo deterministic play-selection**, not raw
root visit argmax. `searchresults.cpp:2048-2064` emits both `visits` (child
visits), `edgeVisits` and `playSelectionValue`. The focused comparator reports:

- `rootVisitArgmaxParity`: Udon's deterministic root move belongs to KataGo's
  maximum raw `edgeVisits` set (ties are represented as a set);
- `rootVisitArgmaxExact`: both sides have one unique raw visit winner;
- `playSelectionParity`: Udon deterministic root move equals
  `moveInfos[0]`;
- sampled Top-1: product temperature sampling, retained for behavior analysis
  only.

This separation prevents a root comparator mismatch from being mistaken for a
search-tree mismatch.

## Trace boundary

The Udon verifier can opt into a bounded root decision tape. Each record stores
the simulation index, parent visits, visited-policy mass, total child weight,
FPU, exploration scale, best/second selection scores, selected edge arithmetic,
and the top eight candidate tuples. The tape is disabled for production and is
only copied into the professional-endgame artifact when a root-trace menu is
used. It is intended to identify the first Udon-side branch difference; it does
not alter visits, model features, or product semantics.

