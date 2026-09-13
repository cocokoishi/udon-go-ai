# Walkable floor and real UI interaction — 2026-09-01

Status: `VERIFIED` for the generated Unity/ClientSim path; not a claim about
physical VR hardware tracking or a built/uploaded VRChat instance.

## Floor

The generator previously removed the collider from
`Default Go Plane · Gallery Floor`, which caused the ClientSim player to fall
through the environment and repeatedly respawn. The generated floor now keeps
its enabled `PlaneCollider`. Both the generator's structural check and
`GoProductionValidator` require that collider.

The real isolated Unity run produced:

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity cells=361
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1
PURE_UDON_GO_CLIENTSIM_PASS ... searchVisits=32 ... encodeGeneration=32
PURE_UDON_GO_CLIENTSIM_RESULT pass=True
```

The ClientSim runtime log contained `RESPAWN_LINES=0`, one initial player
placement line, and zero compiler/Shader/Udon runtime failure patterns:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-floor-runtime-fixed.log
RESPAWN_LINES=0
PLAYER_MOVE_LINES=1
FAILURE_LINES=0
```

## Actual Unity Button path

In the same generated scene, the UI verifier invoked the actual persistent
Unity `Button.onClick` listeners rather than calling Udon endpoints directly.
PVP mode, Black seat claim, separate Chinese/English language replacement,
draw offer, new game clearing, and non-autoplay GPU/PUCT Hint all passed:

```text
PURE_UDON_GO_CLIENTSIM_UI_EVENT button=PVP path=UnityButton.onClick
PURE_UDON_GO_CLIENTSIM_UI_EVENT button=JOIN BLACK path=UnityButton.onClick
PURE_UDON_GO_CLIENTSIM_UI_EVENT button=OFFER DRAW path=UnityButton.onClick
PURE_UDON_GO_CLIENTSIM_UI_HINT_PASS hintMove=R16 ... visits=32 readerCompletedStages=4 ownershipReadbacks=1
PURE_UDON_GO_CLIENTSIM_UI_RESULT pass=True
```

Log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-floor-ui-onclick.log
```

The old runtime verifier's `visits+1` feature-generation assertion was also
updated to the current exact one-fresh-encode-per-completed-visit rule; its
real rerun passed with `encodeGeneration=32`.
