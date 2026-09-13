# State-driven Go UI and bounded refresh — 2026-09-01

Status: `VERIFIED` for the generated Unity/ UdonSharp / ClientSim UI path.

The Go control wall now receives all 29 non-board controls from the generator
as `Button[]` plus action ids. `GoUI.RefreshControlState` derives each
`Button.interactable` value from the live Go mode, seats, turn, match state,
AI state, pending offers, and permissions. The 361 board point buttons are
not included in this control-wall refresh list.

The UI render path caches game/settings/hint/offer/action revisions and AI
phase/output state. Static text and control state are not rebuilt on every
Udon AI tick; active search progress is sampled on a 0.16-second cadence.

## Real ClientSim result

The verifier used actual Unity `Button.onClick.Invoke()` calls with one
persistent Udon listener per button. It checked:

```text
PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=1 interactable=False label=PASS before a seated human turn
PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=34 interactable=True label=START while default PvAI is ready
PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=1 interactable=True label=PASS for the seated Black player
PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=45 interactable=True label=DRAW for the seated PvP player
PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=44 interactable=False label=UNDO before the first move
PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=43 interactable=True label=AI Hint on the active human turn
PURE_UDON_GO_CLIENTSIM_UI_HINT_PASS ... visits=32 ... finalMoveCount=0
PURE_UDON_GO_CLIENTSIM_UI_RESULT pass=True
```

The idle refresh gate observed only four UI rebuilds over 60 ClientSim pump
frames:

```text
PURE_UDON_GO_CLIENTSIM_UI_REFRESH_GATE frames=60 refreshDelta=4
```

During the real 32-visit non-autoplay Hint, the UI recorded 67 bounded dynamic
refreshes while leaving `moveCount` unchanged. The generated scene and
Production Validator also passed after serializing the 29-button binding
arrays.

Runtime log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-ui-refresh-onclick.log
```

This proves state-driven controls and bounded UI work in the isolated
ClientSim environment. It is not physical VRChat/Quest runtime evidence.
