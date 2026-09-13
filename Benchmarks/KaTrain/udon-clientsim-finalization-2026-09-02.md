# Pure Udon Go finalization regression — 2026-09-02

Status: `PASS` for the current generated-world, UdonSharp compile, UI,
search-control, stale-result, Undo/Draw, pool, complete-game, and ClientSim
gravity/floor smoke gates.

This evidence was produced from the current repository source in the isolated
Unity project below. It is real serialized UdonSharp/ClientSim execution, not
an editor-only C# rules substitute.

Environment:

- Unity `2022.3.22f1`
- Direct3D 11 (`-force-d3d11`)
- license entitlement resolved in the Unity logs
- isolated project: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSimPostRecovery`
- generated scene: `Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity`

## Generation and structure

```text
PURE_UDON_GO_GENERATOR_PASS ... cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT ... generatedRoots=1 udonBehaviours=6498 programs=47 compiled=47 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 ... maxVisits=1226
```

The one validator warning records that the raw KataGo oracle is outside the
isolated project root; the committed baked weight asset and manifests were
still validated. The generated pool remains 16 table slots, 3 visible by
default, with two reader channels per table.

## Real ClientSim gates

- UI: `HINT_PASS`, `hintMove=Q3`, `visits=32`, all four GPU readback stages
  complete, `ownershipReadbacks=1`, `finalMoveCount=0`, and
  `analysisUseful=True`. The same-table analysis text contains the player
  fields `AI STATE`, `SEARCH`, `CANDIDATES`, and `WIN PROB`, with no primary
  `REV` diagnostic line.
- UI layout/controls: PvP panel yaw `90`, AI-containing modes yaw `0`, actual
  Unity `Button.onClick` events, separate Chinese/English label arrays, draw
  offer, and state-driven interactable controls all passed.
- Pool: `visible=3->4->3`, `capacity=16`, two independent searches, and fair
  scheduler slots `0,1` passed.
- Stale result: reset after a pending readback passed with
  `revision 2->3`, `moveCount=0`, `aiMoves=0`, `neuralOutputRevision=0`,
  `searchPhase=4`, and `readerState=4`; the verifier accepts either an early
  cancellation with no commit or a later rejected stale callback.
- Undo/Draw: PvAI Undo restored both history planes; PvP Undo and Draw each
  completed through opponent consent.
- Capture presentation: the real rules probe observed the captured stone in
  the bounded board-level sink/shrink animation immediately after the
  authoritative capture (`capturePresentation=True`).
- Modes: PvP/PvAI/AIvP/AIvAI, independent 64/128/514 profile selection,
  starter switching, and two real AIvAI moves passed with 32-visit profiles.
- Complete game: `passed=True`, `moves=372`, `aiMoves=372`, `passMoves=18`,
  `terminalState=2`, and `controllerState=0` in the real Udon AIvAI smoke.
- Floor gravity: the real Udon probe dropped the local player from `y=2` to
  `y=0.004999951` over the generated floor at `y≈0`, observed `grounded=True`
  for 12 consecutive frames, and never crossed below the floor.
- Legal-move matrix: the real Udon rules API returned 361-point masks at 11
  histories across the 160-action corpus; the independent replay compared
  3971 booleans plus state scalars with zero mismatches.

Floor evidence:

```text
PURE_UDON_GO_CLIENTSIM_FLOOR_RESULT pass=True startY=2 finalY=0.004999951 minY=0.004999951 floorY=6.661338E-17 grounded=True sawDrop=True crossedFloor=False stableFrames=12 sampleFrames=13 finalVelocityY=0 failure=
PURE_UDON_GO_CLIENTSIM_FLOOR_PASS
PURE_UDON_GO_CLIENTSIM_FLOOR_FINAL pass=True
```

Exact logs:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-final-generate-v3.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-final-validator-v3.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-ui-analysis-v5.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-final-pool-v3.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-final-stale-v6.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-final-undo-draw-v3.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-final-modes-v4.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-complete-v1.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-floor-v4.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-rules-matrix-v1.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-capture-rules-v2.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-capture-validator-v1.log`

This closes the current local production-slice milestone. It does not claim
physical VRChat/Quest runtime, independent multi-client transport, or balanced
strength calibration; those remain separate evidence gates.
