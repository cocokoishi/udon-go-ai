# Real UdonSharp + ClientSim UI and rules boundary evidence — 2026-08-31

Status: `VERIFIED` for the claims in this report. This is real Unity
2022.3.22f1 with the VRChat SDK/UdonSharp/ClientSim packages and D3D11 GPU,
not an Editor-C# rules harness. The disposable project was:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoRulesCompile
```

The host license was copied unchanged to
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\UnityLicense\Unity_lic.ulf` and
explicitly imported with `-manualLicenseFile`. The license log records
`Successfully processed offline license activation request`, `License file
successfully loaded`, and `Successfully resolved entitlements`.

## Production compile and generator

Final source compile:

```text
PURE_UDON_GO_UDONSHARP_COMPILE_RESULT programs=39 compiled=39 missingSource=0 missingSerialized=0 compilerError=False
PURE_UDON_GO_UDONSHARP_COMPILE_PASS
```

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-final-compile.log`

After TMP resources were present, the generator and Udon world verifier passed:

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity root=PureUdonGo.ProductionRoot cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=763 programs=38 compiled=38 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-rules-generate-3.log`

A second generator execution passed with the same single root, 361 points,
and 763 generated Udon behaviours; no numbered duplicate names were reported.

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-rules-idempotence-generate.log`

Production Validator passed with zero failures and one expected warning that
the raw KataGo oracle is outside the disposable Unity project root:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170-b10c128-s1141046784-d204142634 maxVisits=1226
```

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-rules-validator.log`

## Rules boundary probe

The probe dispatched one Udon custom event and then read the compiled Udon
program variables. All assertions ran through the real `GoGame` and
`GoSearchState` methods:

```text
PURE_UDON_GO_CLIENTSIM_RULES_RESULT capture=True doublePass=True suicide=True ko=True simpleKo=True searchKo=True multiSuicidePrisoner=True pureRulesApi=True capacity=True handicap=True resign=True captureMoves=3 terminalState=2 suicideMoves=8 koLocation=20 koMoveCount=13 whitePrisonersAfterSuicide=3 handicapCount=5 resignState=4 failure=
PURE_UDON_GO_CLIENTSIM_RULES_PASS
PURE_UDON_GO_CLIENTSIM_RULES_RESULT pass=True
```

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-rules-boundary-3.log`

This specifically verifies that disabling positional superko still leaves
simple-ko protection, MCTS state rejects the same ko recapture, a connected
three-stone suicide is booked as three opposing prisoners, pure rules
legality is independent of match/seat permissions, and the capacity boundary
terminates rather than leaving a stuck PLAYING state.

The matching Python reference tests pass `11/11`; the KataGo feature tests
pass `4/4`.

## Generated UI, language, mode, and hint path

The actual generated production camera rendered separate single-language
screenshots:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\pure-udon-go-current-ui-en.png
E:\UnityPRJS\ShaderGPT_neoversion\Temp\pure-udon-go-current-ui-zh.png
```

The English and Chinese screenshots show the same three-screen control wall;
the language action replaces each registered TMP text in place instead of
stacking bilingual labels. The left screen retains PvP/PvAI/AIvP/AIvAI,
seats, start/pass/resign/reset, both starters and AI Hint. The right screen
retains independent Black/White profiles and `32/64/128/514` visits.

The real ClientSim UI verifier dispatched generated `GoUiButton.Press`
events, not direct game method calls, and passed:

```text
PURE_UDON_GO_CLIENTSIM_UI_PVP_PASS panelYaw=90
PURE_UDON_GO_CLIENTSIM_UI_LANGUAGE_PASS english=False arrays=parallel
PURE_UDON_GO_CLIENTSIM_UI_HINT_PASS hintMove=D4 baselineMoveCount=0 finalMoveCount=0 baselineAiMoves=0 finalAiMoves=0 visits=32 readerCompletedStages=5 revisionBefore=2 revisionAfter=4
PURE_UDON_GO_CLIENTSIM_UI_RESULT pass=True
```

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-ui-hint-4.log`

The mode/difficulty verifier additionally passed the control-wall policy:

```text
PURE_UDON_GO_CLIENTSIM_MODES_INITIAL ... panelYaw=0
PURE_UDON_GO_CLIENTSIM_STATE mode=PvP panelYaw=90
PURE_UDON_GO_CLIENTSIM_STATE mode=AIvP panelYaw=180
PURE_UDON_GO_CLIENTSIM_STATE mode=AIvAI panelYaw=180
PURE_UDON_GO_CLIENTSIM_MODES_PASS modes=PvP,PvAI,AIvP,AIvAI starter=BLACK blackVisits=32 whiteVisits=32 minAiMoves=2 moveCount=2 aiMoves=2 searchVisits=32 readerCompletedStages=5
PURE_UDON_GO_CLIENTSIM_MODES_RESULT pass=True
```

Log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-modes-rotation.log`

These results verify the current UI/rules checkpoint only. They do not claim
VRChat device runtime, independent-client transport, full randomized KataGo
feature equivalence, or external strength calibration.
