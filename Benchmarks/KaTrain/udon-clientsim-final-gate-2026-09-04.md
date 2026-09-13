# Pure Udon Go — current ClientSim gate ledger (2026-09-04)

This is a conservative index of the current checkout's real Unity/UdonSharp
ClientSim evidence. It does not turn ClientSim into proof of a clean VRChat
client, Quest support, two-client transport, model redistribution rights, or
playing strength.

Environment: Unity 2022.3.22f1, Windows D3D11, generated schema 5, production
model `g170e-b10c128-s1141046784-d204142634`. The copied offline license was
used from the disposable Temp project; its contents are not recorded here.
Raw logs are under:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\`.

## PASS evidence

| gate | observed evidence |
| --- | --- |
| generator / validator | generator `cells=361`, UltraHard `514`, engine max `1226`; validator `failures=0`, one raw-oracle-location warning |
| runtime | exact `32/32` visits, real GPU path, `aiMoves=1`, legal `Q16` commit |
| rules / trace / matrix | rules and presentation assertions pass; trace `160/160` accepted; matrix `11` positions / `3971` legal queries |
| feature corpus | `16` ClientSim cases pass; current log records the captured export; the independent NPZ differential is retained in the dated feature reports |
| cooperative Superko | `361` points, `9` frames, max `48` points/frame, `0` mismatches; cancellation invalidated the old token |
| GPU graph | `84` passes over `44` frames, max `10` passes/frame, visited stage mask `1022`, cancellation stopped later submissions |
| derived history / payload | `0` hash, stone-count, legal, Superko, and feature mismatches; estimated raw int payload `12,564` bytes vs legacy `53,544` |
| ownership / stale / recovery | ownership transfer and stale-result gates pass; network recovery clears stale offers/seats; three-reader A/B/C quarantine gate passes with two rotations and stale callbacks discarded |
| undo / draw / UI / floor | transactional corruption rollback, PvP consent, no-capture reset/undo presentation, advanced panel default collapse, and gravity/floor settling pass |
| BoardPool / modes / quality | 16-slot pool, 3 visible by default, lazy heavy allocation, two independent searches, all four modes, and PUCT divergence smoke pass |
| public difficulty ladder | exact `32`, `64`, `128`, and `514` visits complete; no budget was lowered |

## Measured performance (not a hard release gate for this run)

The user explicitly relaxed the historical 11/20-second latency target. Exact
visit accounting is retained; the measurements are still reported rather than
relabelled as a pass.

| profile | elapsed | frame p95 / p99 / max | controller max | Superko / feature / GPU-submit / MCTS / UI max (ms) |
| --- | ---: | ---: | ---: | --- |
| 32 visits | 18.957 s | 24.495 / 54.793 / 1738.808 ms | 128.071 ms | 4.025 / 2.869 / 0.950 / 2.680 / 9.075 |
| 64 visits | 30.924 s | 20.458 / 33.398 / 1588.392 ms | 121.750 ms | 4.049 / 2.552 / 0.912 / 3.479 / 10.172 |
| 128 visits | 51.703 s (ladder) | not collected in this sample | — | — |
| 514 visits | 212.841 s (ladder) | not collected in this sample | — | — |

The 32/64 profiler samples completed exactly `32/64` visits but exceeded the
historical end-to-end thresholds. Two- and three-table percentile profilers
did not finish inside their 300-second ClientSim budget and remain open.

## OPEN / FAIL evidence

- High-budget calibration: sample 7, scenario 3, UltraHard stopped at `278`
  visits under the retained timeout. This is a real failure, not an extended
  timeout disguised as success.
- Deterministic long-history stress: the 64-ply case exceeded its 90-second
  case budget; 128/256/520-ply cases were not reported as passing.
- Full readback-recovery verifier: ClientSim hit `OutOfMemoryException` in
  Udon serialization before the fixture ran. The independent three-reader
  gate above remains valid evidence for the implemented pool, but this
  verifier is open.
- Complete-game lifecycle smoke exceeded the 20-minute execution window and
  is open; it never replaces the public 32/64/128/514 ladder.
- The current neural export intentionally says `numericComparison=not-performed`.
  Dated independent numeric reports exist for the earlier equivalent graph,
  but a fresh post-stage numeric comparator run is not claimed here.

