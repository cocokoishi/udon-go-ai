# Pure Udon Xiangqi -> Pure Udon Go Porting Blueprint

Purpose: give future agents the important reusable Xiangqi engineering patterns without forcing repeated full-project reading. This is a READ-ONLY reference map. Port the architecture/semantics, not chess rules or Xiangqi-specific presentation.

Authorized reference repository/project must never be modified.

---

## 1. Highest-value Xiangqi files

### `Runtime/XiangqiGame.cs`

Primary source for mature synchronized match behavior.

Study these methods/patterns:

- `OnDeserialization`
- `OnOwnershipTransferred`
- `OnPlayerJoined`
- `OnPlayerLeft`
- `RecoverNetworkState`
- `SanitizeSynchronizedState`
- `CanLocalForceReset`
- `RequireLocalMatchControl`
- `ClaimSide`
- `ReleaseLocalSeat`
- `RequestStartMatch`
- `ToggleAIHints`
- `CycleAIHintDifficulty`
- `RequestAIHint`
- `RequestForceReset`
- `SetControllerConfiguration`
- `RequestUndo`
- `OfferOrAcceptDraw`
- `ResignLocal`
- `ApplyAIConfigurationForSide`
- `SerializeAndRefresh`
- `StartAIIfNeeded` / local search invalidation paths

### `Runtime/XiangqiUI.cs`

Primary source for state-driven UI behavior.

Study:

- revision-based refresh rather than unconditional per-frame redraw;
- `RefreshControlState`;
- explicit `Button.interactable` state;
- show/hide controls by mode;
- AI Hint locked/armed/searching labels;
- force-reset double-confirm state;
- per-side AI settings apply buttons;
- transient user-readable rejection/status messages;
- language replacement without stacked bilingual labels.

Do NOT copy Xiangqi's panel yaw rule into Go. Go has an explicit override: only PvP is side-mounted.

### `Runtime/XiangqiBoardPool.cs`

Primary source for table pool and shared GPU scheduling concepts.

Important semantics:

```text
MinimumVisibleBoards = 1
InitialVisibleBoards = 3
visible board count is synchronized
tables are pre-generated
append/remove changes visibility
active searches share a measured frame budget
scheduler uses active-search count, stride and slot
hidden tables do not participate
```

Go must extend capacity to 16 while preserving these principles.

---

## 2. Synchronization pattern to port almost literally

### Owner-authoritative state

The table owner is the only client allowed to publish authoritative game mutations. Human clients request actions; ownership is taken only through explicit permission paths.

For Go, the synchronized state must cover not only the board but every value required to reconstruct legal play and KataGo input history:

```text
board
previous board/history
recent moves
position hashes/history
sideToMove
moveCount
ko
captures
consecutive passes
komi/rules when mutable
terminal state/result
seats
mode
starter
AI settings
hint permission/result
undo offer
draw offer
revisions
network recovery revision
audio event revision
```

Never synchronize the MCTS tree or intermediate NN tensors.

---

## 3. Seat identity and reconnect recovery

Xiangqi does not trust `playerId` alone. It uses player id + display name and checks whether the referenced player is actually still connected.

Port these principles:

1. A seat has both synchronized id and name.
2. A reconnect can get a different player id.
3. A joining player may arrive before the stale connection's leave callback.
4. The owner may clear a stale seat that references the reconnecting identity.
5. A currently connected same-name player must not be displaced just because a name matches.
6. A player cannot occupy both sides.
7. AI-controlled sides cannot retain human seat occupants.
8. After join/leave, schedule recovery more than once because VRChat callback ordering and ownership transfer are not guaranteed.
9. Only the eventual owner actually mutates repair state.
10. Any real repair increments normal revision + `networkRecoveryRevision`.

Go-specific addition: pending undo/draw requests referencing a stale/disconnected player must be cleared during repair.

---

## 4. Lifecycle behavior

### `OnDeserialization`

Xiangqi's mature principle:

```text
receive authoritative snapshot
-> cancel local search belonging to old snapshot
-> clear invalid local selection
-> refresh UI/view
-> if local client is owner, schedule delayed recovery
```

Go must additionally invalidate feature encoder, GPU readback, search token and cached hint bound to the previous revision/hash.

### `OnOwnershipTransferred`

Principle:

```text
if ownership becomes local:
    schedule recovery after a few frames
else:
    cancel local search
    refresh from state
```

Go must also increment an owner-lifecycle identity used by every async NN result so an old owner can never commit a late callback.

### `OnPlayerJoined` / `OnPlayerLeft`

Xiangqi repairs seats on the owner and schedules delayed recovery on every client. The current Go implementation already moves in this direction; preserve it and make undo/draw/hint/search state participate in the same recovery invariant.

---

## 5. Permission model

Copy Xiangqi's distinction between:

- seated player;
- empty/vacant table controller;
- AI-AI controller authority;
- object/state owner;
- instance master;
- spectator.

A spectator must not be able to overwrite an active human game.

Use one central permission helper equivalent to `RequireLocalMatchControl` instead of ad-hoc checks in each button.

Mode/start/reset/settings changes require appropriate control.

Normal move/pass/resign/undo/draw require the relevant seat identity.

---

## 6. AI-AI control authority

Xiangqi keeps a synchronized AI control authority identity because AI-AI has no human seat holder.

Go should retain the same concept:

```text
aiControlAuthorityPlayerId
aiControlAuthorityName
```

Use it for:

- AI-AI start/reset/mode/settings control;
- reconnect fallback;
- preventing arbitrary spectators from fighting over AI-AI table ownership.

The name is a reconnect fallback, not permission to evict a live different player.

---

## 7. AI Hint lock model

This is one of the most important Xiangqi behaviors to port.

Xiangqi semantics:

```text
before start:
    user may enable/disable AI hints
    user may choose hint strength

at start:
    permission becomes frozen

after start:
    enabled -> seated human may request on human turn
    disabled -> cannot be enabled mid-game
```

Go should expose the state explicitly:

```text
AI Hint · Off
AI Hint · On
AI Hint · Armed
AI Hint · Locked for this game
AI Hint · Searching
AI Hint · recommends Q16
```

During a hint search, board input for that decision point should be locked. A second-click confirmation can stop and show the current valid result, matching the Xiangqi interaction concept.

Every hint is bound to revision/settings/owner lifecycle/side/hash and never auto-plays.

---

## 8. Undo: use Xiangqi rollback mechanics, improve consent semantics

Xiangqi already demonstrates the important mechanical part: cancel search, rollback history, restore playing state, clear draw/search state, increment revision, serialize, then restart AI if needed.

Go must make the rollback deeper because superko/KataGo history matters.

The rollback unit must restore:

```text
board
previous boards
recent move planes/history
position hashes
positionHistoryCount
sideToMove
moveCount
ko
captures
consecutivePasses
terminal/result state
hint state
all data used by feature encoding
```

PvAI/AIvP may undo enough plies to return to the human decision point.

Unlike current Xiangqi, PvP Go must NOT execute undo immediately. It must synchronize an offer and require opponent acceptance.

Suggested synchronized fields:

```text
undoOfferSide
undoOfferRevision
undoOfferMoveCount
```

Rules:

- requester cannot accept own offer;
- only opposite seated player accepts;
- any new move/pass/reset/mode change/end/disconnect invalidates it;
- acceptance verifies target revision/move count before rollback.

---

## 9. Draw and resign

Xiangqi's `OfferOrAcceptDraw` provides a useful product pattern:

- first seated PvP player offers;
- opposite player accepts;
- local cooldown avoids spam;
- offer is synchronized;
- terminal draw cancels search and publishes game-end audio.

Port that pattern to Go.

Pending draw offer must be cleared by new move/pass/reset/mode change/disconnect.

For PvAI, only implement AI acceptance if it is based on a documented real evaluation threshold. Otherwise reject with a clear message.

`ResignLocal` pattern also ports cleanly: only the seated local side may resign; spectator cannot.

---

## 10. State-driven UI

Xiangqi's UI is useful because visible controls reflect real state instead of relying on backend rejection after the click.

Go should have a `RefreshControlState` equivalent that controls:

```text
Start
Pass
Undo / Request Undo / Accept Undo
Draw / Offer Draw / Accept Draw
Resign
Join Black
Join White
Leave Seat
AI Hint enable/lock/request
Hint strength
Force Reset confirmation
AI profile target/apply
```

Do not permanently show impossible controls.

A disabled button should have an understandable status/reason.

Go should remove redundant top-level controller toggles from the main left panel because the four mode buttons already define the controller configuration.

---

## 11. Dirty/revision UI refresh

Xiangqi refreshes UI on relevant revision/state changes and bounded intervals rather than rebuilding every TMP string every frame.

Port this aggressively.

Suggested Go local caches:

```text
displayedGameRevision
displayedSettingsRevision
displayedPoolRevision
displayedHintRevision
displayedOfferRevision / offer tuple
last telemetry refresh time
last slider values
transient message deadline
```

Active search telemetry can update a few times per second. Everything else should be event/dirty-driven.

Do not recompute current board score every frame solely for display.

---

## 12. Board pool and GPU scheduler

Xiangqi's board pool measures observed frame time and active-search submit cost, then assigns scheduler stride/slot.

Go needs the same global concept but must account for its much heavier KataGo NN graph.

Port these concepts:

```text
visible board count
active search count
observed frame milliseconds
safe frame submit budget
searches/dispatches admitted per frame
round-robin slot
stride
```

Go-specific rules:

- 16 slots generated, 3 visible by default;
- hidden table = inactive GameObject and zero search/UI/raycast work;
- share immutable weights/materials;
- never share mutable MCTS/search token/readback state;
- use lazy GPU resource allocation;
- hiding/removing table invalidates outstanding GPU work;
- fairness: one busy table must not starve another forever;
- throughput optimizations must preserve exact visit semantics.

---

## 13. What NOT to port from Xiangqi

Do not mechanically copy:

- red/black chess terminology;
- chess move history encoding;
- chess draw/repetition rules;
- alpha-beta/PVS/quiescence parameters;
- Xiangqi panel orientation for AIvP/AIvAI;
- chess-specific engine counters as Go's primary UI;
- any rule assumption that bypasses Go positional superko/history.

Go's panel orientation is fixed by product requirement:

```text
PvP=90 degrees
PvAI=0
AIvP=0
AIvAI=0
```

---

## 14. Evidence standard

A Xiangqi-inspired implementation is not accepted because source code looks similar.

For networking claim, prove:

- two-player seat synchronization;
- remote move propagation;
- mid-game spectator join snapshot;
- reconnect/stale-seat repair;
- ownership transfer during AI search;
- old-owner result rejection;
- PvP undo consent;
- draw consent;
- hint permission lock;
- AI-AI controller transfer;
- board-pool visible count synchronization;
- multiple active table searches without cross-table contamination.

For UI claim, test actual Unity Button `onClick`/interaction path. Direct Udon `SendCustomEvent("Press")` alone is not enough.

For performance claim, preserve exact 32/64 visit semantics and the numerical/search correctness gates in `AGENTS.md`.
