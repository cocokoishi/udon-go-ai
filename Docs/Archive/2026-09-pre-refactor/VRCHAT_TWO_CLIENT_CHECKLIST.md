# Pure Udon Go real-client verification checklist

This checklist is an executable release gate for two real VRChat PC clients.
ClientSim is useful regression evidence, but does not close this gate. Record
client build, world build, GPU, date, table number, and both player identities
with every run. Do not mark an item passed without retained logs or video.

## Preconditions

- Generate the scene from `Tools > Pure Udon Go > Generate Final Production Scene`.
- Run Production Validator with zero failures.
- Upload a private test build from the exact tested checkout.
- Enable Advanced Analysis only when collecting diagnostics.
- Keep public difficulties at the generated values: Beginner/4, Advanced/32,
  Master/128, and Ultrahard/384 visits.

## Join, seats, and ordinary play

1. Client A joins, then client B joins.
2. A takes Black and B takes White in PvP.
3. Confirm board, side, move count, rules/settings revision, seats, names,
   difficulty, hint permission and authority agree on both clients.
4. Play ordinary moves, single capture, multi-capture, pass, undo offer and
   acceptance, draw offer and decline, and resign.
5. Confirm capture presentation occurs only for actual captured stones; undo,
   reset and deserialization do not present captures.

## AI, hint, and settings

1. Start PvAI and complete an AI turn at the default Beginner/4 visits.
2. Apply a difficulty change before a new match and verify settings revision
   and exact target visits on both clients.
3. Enable hints before start, request a hint on a human turn, and confirm the
   hint does not auto-play. Repeat with hints locked before start.
4. Confirm there is exactly one authoritative AI move and no duplicate move.

## Ownership interruption matrix

For each row, start work on A, make A leave, wait for B to become owner, and
complete one subsequent legal AI action on B:

| interruption point | telemetry to retain | required result |
| --- | --- | --- |
| superko mask | probe index/frame/token/revisions | old mask stops; B restarts |
| feature encode | encoder state/generation | old encode cannot publish |
| GPU graph | stage/progress/graph identity | old graph stops submitting |
| async readback | reader A/B/C state and callback counters | stale callback discarded; recovered reader reusable |
| MCTS selection/expansion/backup | phase/visits/root identity | old tree cannot commit |

For every row verify: no duplicate AI move, no stale move or hint, no dead
controller, no corrupted superko cache, no stuck seat, and no stale telemetry.

## Late join

1. After a capture, a pass, a difficulty change, and at least 32 plies, join
   client C.
2. Compare A/B/C: board, previous boards, side, move history/count, captures,
   passes, ko, game state, rules/settings revisions, difficulty, seats, AI
   authority and hint state.
3. Rebuild local derived history on C and compare the four hash lanes,
   stone-count history, legal/superko masks and feature history planes.
4. Continue play and complete an AI turn to prove the rebuilt client is not
   stuck or using stale state.

## Required evidence record

Record `PASS`, `FAIL`, or `OPEN` for each numbered item, plus frame-time
min/mean/p50/p95/p99/max while AI is active and each stage maximum. A missing
second client, missing Quest/Android device, or missing clean client profiler
must remain `OPEN`; it is never replaced by a ClientSim result.
