# PureUdonGo active documentation

This directory contains the current implementation contract. It is intentionally
small. Old milestone/knowledge documents live under `Archive/` and
`PROGRESS.md` is a dated evidence ledger only.

## Source-of-truth order

1. current production code and executable verifier results;
2. root [`AGENTS.md`](../AGENTS.md);
3. [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md);
4. the active domain documents below;
5. immutable benchmark/evidence artifacts;
6. `PROGRESS.md` and `Archive/**` as historical evidence only.

## Active documents

- [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) — exact phase-by-phase code
  edits and verifier migrations. Start here after `AGENTS.md`.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — target class boundaries and data flow.
- [`UI_AND_SETTINGS.md`](UI_AND_SETTINGS.md) — exact Draft/Apply model and
  visits-only Custom semantics.
- [`NETWORKING.md`](NETWORKING.md) — owner lifecycle and how to verify remote
  semantics inside one ClientSim VM.
- [`SEARCH_SEMANTICS.md`](SEARCH_SEMANTICS.md) — rule/V7/NN/MCTS invariants that
  optimization may not change.
- [`PERFORMANCE.md`](PERFORMANCE.md) — fixed A-H corpus, metrics, and the ordered
  optimization sequence.
- [`VERIFICATION.md`](VERIFICATION.md) — current single-Unity required gate
  matrix and non-blocking future manual checks.
- [`REFACTOR_AUDIT.md`](REFACTOR_AUDIT.md) — current-to-target code map and
  remaining migration debt.
- [`LOCAL_ENVIRONMENT.md`](LOCAL_ENVIRONMENT.md) — actionable local environment
  facts only.
- [`PLATFORM_STATUS.md`](PLATFORM_STATUS.md) — evidence scope; unavailable
  real-client platforms are future manual validation.
- [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) — attribution/license
  information.

## Test-environment boundary

The implementation agent is expected to complete all current work using one
Unity Editor and one ClientSim VM.

Current verification may use:

- ClientSim simulated remote players;
- `Networking.SetOwner` ownership transfer;
- `ClientSimMain.RemovePlayer` leave/recovery;
- source/replica table instances in the same generated scene;
- backing `UdonBehaviour.SetProgramVariable` plus the real deserialization path;
- a real pending GPU request plus the existing verifier callback hook when
  ClientSim suppresses a late callback;
- 1/2/3 simultaneous tables inside the same ClientSim.

Real two-client VRChat, PCVR and Quest are **not** blockers for the current
implementation. They are recorded as `FUTURE_MANUAL` only.

## Current engineering priority

Do not begin another broad speculative optimization pass. Follow
`IMPLEMENTATION_PLAN.md`:

1. deterministic shared-base Custom settings;
2. remove selected-side/hidden settings migration debt;
3. port proven group/liberty generation stamps from `GoSearchState` to `GoGame`;
4. centralize room-wide move-mask warm-up;
5. use live GPU submit telemetry in BoardPool scheduling;
6. exact history buckets;
7. ladder delta journal;
8. minimal reader-recovery fixture;
9. fixed A-H 4/32 performance corpus;
10. only then use measured thresholds for input-upload/deeper MCTS work.
