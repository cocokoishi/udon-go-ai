# Go undo, draw and UI interaction — 2026-09-01

Status: `VERIFIED` for the covered single-client Udon/ClientSim paths.

## Udon state probe

`GoUndoDrawProbe` runs its mutations over multiple Udon frames and calls the
production `GoGame` methods. It verified:

```text
pvAiUndo=True
pvpUndoConsent=True
pvpDrawConsent=True
PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_PASS
```

The PvAI case records two moves, invokes `RequestUndo`, and verifies that the
packed move log is replayed back to the empty position with
`positionHistoryCount=1`, Black to move, and the match still active. The PvP
cases verify that the first player creates a synchronized offer and cannot
accept their own undo/draw offer. Opponent acceptance across two independent
VRChat clients remains a separate `NOT RUNTIME VERIFIED` gate.

## Generated Button path

The UI probe invokes the generated Unity `Button.onClick` event. Each button
has exactly one persistent Udon listener installed by the generator; the
probe does not call the endpoint's `SendCustomEvent` directly.

```text
PVP path=UnityButton.onClick
JOIN BLACK path=UnityButton.onClick
LANGUAGE TO CHINESE path=UnityButton.onClick
LANGUAGE TO ENGLISH path=UnityButton.onClick
OFFER DRAW path=UnityButton.onClick
DRAW_PASS offerSide=1 moveCount=0
NEW GAME path=UnityButton.onClick
AI HINT path=UnityButton.onClick
HINT_PASS visits=32 readerCompletedStages=4 ownershipReadbacks=1 finalMoveCount=0
PURE_UDON_GO_CLIENTSIM_UI_RESULT pass=True
```

The UI result also verifies the Go-specific rule that only the two-human PvP
mode rotates the panel to the side (`90` degrees); the AI modes remain on the
rear control wall. Chinese and English are separate active-language strings,
not two labels rendered together.

Complete logs are retained in the allowed Temp root:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-undo-draw-probe.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-ui-real-button-onclick.log`
