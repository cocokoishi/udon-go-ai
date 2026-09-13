# Real Udon ClientSim complete AIvAI game — 2026-08-31

Status: `VERIFIED` for a complete Udon-driven AIvAI game and terminal scoring;
`NOT STRENGTH CALIBRATED`.

Compile/generator gate:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-42.log`

Runtime log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-complete-2.log`

The temporary Udon probe configured both real `GoAiController` sides as
`Custom=1 visit`, started an AIvAI match through `GoGame`, and only observed
the public Udon state. Search, feature encoding, GPU shader evaluation, five
output readback stages, PUCT transition and move commit were performed by the
production Udon objects. The custom one-visit setting is deliberately a test
profile so a complete 19×19 game can be exercised in finite time; it does not
change the public Beginner/Advanced/Master/UltraHard presets.

Observed terminal result:

```text
PURE_UDON_GO_CLIENTSIM_COMPLETE_RESULT passed=True moves=372 aiMoves=372 passMoves=18 terminalState=2 winner=-1 controllerState=0 failure=
PURE_UDON_GO_CLIENTSIM_COMPLETE_PASS
PURE_UDON_GO_CLIENTSIM_COMPLETE_RESULT pass=True
```

The process exited normally after approximately 798.490 seconds of log
lifetime. `ERROR_MARKERS=0` for Udon runtime exceptions, index errors,
compiler errors, and the complete-game verifier failure marker. The game
reached a non-playing terminal state after 372 legal AI commits and recorded
18 pass moves, including the required two-pass ending path.

This is complete-game runtime evidence, not a public-preset strength result:
one custom one-visit game cannot establish Elo, monotonic difficulty strength,
oracle rank, or a balanced self-play sample. Those require multiple controlled
games and position-level comparisons against the authorized KaTrain/KataGo
oracle.
