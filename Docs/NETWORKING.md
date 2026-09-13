# Authority, synchronization and single-ClientSim lifecycle testing

This document defines networking semantics the code must preserve and how the
current implementation agent verifies them with one Unity/ClientSim VM.

Real independent VRChat clients are future manual validation and are not current
DoD blockers.

## 1. Authority domains

Each table has one authoritative network owner at a time.

`GoGame` owns synchronized match/position state:

- board and previous boards;
- compact accepted move history and rule scalars;
- side-to-move/game state;
- seats/controllers/starter;
- hint, undo/draw and final result state;
- position/lifecycle `revision`.

`GoAiSettings` separately owns the synchronized AI user choices:

```text
sharedPreset
blackUseCustom
blackCustomVisits
whiteUseCustom
whiteCustomVisits
configRevision
```

Search trees, feature buffers, GPU textures, reader state and UI drafts are
local.

### Payload accounting

`GoGame.EstimatedGoGameIntArrayBytes` is only the raw int-array lower bound for
the compact position domain; it is not a whole-table snapshot estimate.
`GoGame.OnPostSerialization` records the actual owner-side byte count for that
domain. `GoTelemetry` separately records its own `OnPostSerialization` byte
count and exposes `EstimatedRawSyncedBytes()` (a transparent raw scalar/string
estimate) before the first publish. Reports must name these domains separately
and must not add the legacy derived hash arrays back into the synchronized
position payload.

## 2. Revision meanings

`GoGame.revision` identifies position/lifecycle. Real board/reset/undo/mode
changes that make a root irrelevant may invalidate a search.

`GoAiSettings.configRevision` identifies applied configuration. A newer config
alone does **not** invalidate an already-running valid search.

`GoAiController.ownerLifecycle` identifies the local authority generation.
Ownership transfer/loss invalidates old-owner async work even if board state is
unchanged.

A search/evaluation also carries token, root side/hash and expected node
identity. These values protect against stale callbacks.

## 3. Deterministic settings reconstruction

Identical `GoAiSettings` must resolve identical complete profiles on every local
reconstruction.

Custom inherits the complete `sharedPreset` parameters and overrides only
visits/query count. No non-visit search knob may depend on local profile residue.

This is how the current task proves the future late-join property without a real
second client: poison a local profile, assign the incoming synchronized settings
values to the backing Udon program, run the actual deserialization path, and
assert full profile equality.

## 4. Apply/deserialization during search

A current SearchSession owns its captured profile.

Both local Apply and simulated remote settings deserialization must:

- update applied settings for the next search;
- refresh local profile views/UI;
- leave a valid current token/root/profile unchanged;
- not call configuration-driven `InvalidateSearch()`.

The single-ClientSim lifecycle test must continue until the **next real search**
starts and proves it captured the new complete profile.

## 5. Stale async work

Reject old work for real identity/lifecycle mismatches:

- search token;
- position revision/root side/hash;
- owner lifecycle;
- expected node/evaluation identity;
- explicit cancellation;
- game state no longer accepting the result.

Do not reject only because global config revision changed.

Repeat the owner/root checks immediately before move and live telemetry commit.

## 6. Ownership testing in one ClientSim

Use the existing pattern in `GoClientSimOwnershipVerifier`:

1. `ClientSimMain.SpawnRemotePlayer`;
2. begin a real local AI search and reach a real pending readback;
3. record move count, AI moves, owner lifecycle and reader/search identity;
4. `Networking.SetOwner(remotePlayer, gameObject)` and transfer reader sibling
   ownership as required by the current generated layout;
5. assert owner lifecycle advances and the old local worker cannot commit;
6. observe cancellation/quarantine/stale handling;
7. return ownership to the local player;
8. assert a fresh local search can resume and commit.

This is the current automated authority gate. Label it single-VM simulation, but
it is a required current test.

## 7. Player leave/recovery testing in one ClientSim

Use the existing `GoClientSimNetworkRecoveryVerifier` pattern:

```text
SpawnRemotePlayer
prepare seat/offer/AI authority fixture
RemovePlayer(remote)
observe actual Udon player-left/recovery callbacks
assert seat/offer/authority cleanup and recovery revisions
```

Keep this gate; do not replace it with a prose requirement for unavailable real
clients.

## 8. Permission guard

Use the existing remote-player fixture to place a real table under simulated
remote ownership and make the local player an unseated spectator. Destructive
commands must be disabled and side-effect free at the production command layer,
not merely hidden by UI.

## 9. Reader A/B/C

One neural evaluation uses one packed async readback.

A cancelled reader remains quarantined until its late callback is consumed as
stale:

```text
WAITING -> QUARANTINED -> late callback -> RECOVERED -> reusable
```

Keep the existing A/B/C rotation/recovery assertions. Move the verifier to a
minimal one-table fixture to eliminate full production-scene serialization OOM.
If ClientSim suppresses the late platform callback, the existing verifier hook
may be called only after a real pending request created quarantine.

## 10. Simulated remote settings deserialization

Add this to the settings lifecycle verifier rather than requiring another VM:

1. start a real search;
2. write a newer synchronized settings snapshot into the settings backing
   `UdonBehaviour` as an incoming state fixture;
3. send/invoke the production deserialization event/path;
4. assert current search token/profile remains old;
5. assert local applied settings/profile view becomes the deterministic new
   profile;
6. after current search finishes, assert next real search captures the new
   profile.

## 11. Future manual validation

Record but do not block `READY_SINGLE_CLIENTSIM` on:

- two independent VRChat processes;
- actual network late join/reconnect transport;
- network latency/serialization packet behavior outside ClientSim;
- PCVR interaction;
- Quest.

These checks are useful later, but the implementation agent must not stop current
work because they are unavailable.

## 12. Per-table compute isolation

The generated world owns one complete `GoGame`/`GoAiController`/search/encoder/
GPU/readback set per table. `GoBoardPool` only admits visible, active
controllers; `GoAiController.Tick()` performs the final
`Networking.IsOwner(game.gameObject)` check before any search transition. A
match-start warm-up follows the same rule and is skipped when the table has no
AI side. Thus a human-vs-human table, a spectator, or an inactive hidden table
does not borrow another table's search state or GPU resources.

`GoGame` remains the sole authority for board, seats, controller mode, match
revision and accepted moves. `GoAiSettings` is a separate per-table authority
for the deterministic AI profile. Position snapshots rebuild local derived
history; configuration-only snapshots refresh profiles without invalidating a
valid search. Ownership transfer increments the controller lifecycle and
cancels the old owner's pending work before a new search can commit.

This is the single-VM/static guarantee. Independent real VRChat clients,
transport latency and PCVR interaction remain `FUTURE_MANUAL` validation.

## 13. Multiplayer lobby and stalled-turn policy (static scenario matrix)

The following traces are derived from the production call path
`GoUI.HandleAction -> GoGame` and are intentionally written as a static
review artifact. Each visible table has its own `GoGame` authority domain;
there is no shared seat or search state across tables.

| Scenario | Authoritative result | Other clients observe |
| --- | --- | --- |
| Player A joins one or more seats before Start Match | `ClaimSide` takes the table owner and serializes seat id/name plus `revision` | Seat label, mode and enabled controls update after `OnDeserialization` |
| Player B/C joins while a table is still an empty, unstarted lobby | Pre-match lobby claims may replace an occupied human seat, matching the Xiangqi lobby contract; the last accepted owner snapshot wins | All clients converge on the accepted seat identity; no local-only seat is kept |
| A visitor sees an occupied, unstarted lobby | Join Black/White remains an enabled command, but mode/starter/settings/handicap writes require an empty lobby, a seat holder, the table owner or master | A visitor can take an unstarted seat without being able to rewrite another player's draft configuration |
| Match starts and moves are played | `RequestStartMatch`, `TryPlayInternal`/`PassInternal` serialize mode, starter, board, compact move log, side, captures, ko, revision and turn timer | Late joiners rebuild derived hashes/stone counts locally and render the same board/turn/history |
| Match ends by score, resign or move limit | `gameState`, winner/final score, last action and `turnStartedServerSecond=0` are serialized | The table is a public recovery surface; any participant can arm Force Reset |
| PvP live turn, before 50 seconds | Only instance/table owner or the human player whose turn it is may pass, resign or reset; spectators receive a denial and cannot mutate state | Denied calls remain side-effect free; a stale spectator cannot cancel the owner's AI/search session |
| PvP/AI live turn stalled for 50 seconds | The synchronized server-second timer makes `IsForceResetTimeoutOpen()` true for every client | Any participant may use the two-click Force Reset recovery path; no automatic reset is performed |
| Player leaves during a match | Owner `OnPlayerLeft` clears only that identity, increments recovery/position revision and republishes; pending search is invalidated by revision/owner lifecycle checks | Remaining clients converge on released seat and preserved board; a new player may claim the vacant PvP seat |

Force Reset is a local two-click confirmation window of four seconds. The first
click only arms the control and changes its label; the second click calls the
authoritative `RequestForceReset`. `CanLocalForceReset` is rechecked in both
the UI and game domain, so direct Udon calls cannot bypass the live-turn guard.
Handicap placement is also treated as an authoritative position rewrite and
therefore uses the same empty-lobby/seat/owner permission boundary; it cannot
be used as an alternate spectator reset endpoint.
