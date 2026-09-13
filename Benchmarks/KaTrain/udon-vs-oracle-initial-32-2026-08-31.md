# Udon vs KaTrain oracle — initial position, 32 visits

`LOCAL / SYNTHETIC` and `VERIFIED` for the two independently executed sides;
this is not a strength calibration result. The Udon production model and the
KaTrain-configured oracle model are different artifacts, so the comparison is
useful as a coarse move-quality smoke check only.

## Udon ClientSim observation

Source log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-14.log`

```text
PURE_UDON_GO_CLIENTSIM_PASS matchStarted=True revision=3 moveCount=1 aiMoves=1 searchVisits=32 controllerState=0 lastMoveLoc=60 lastMove=D16 searchBestMove=D16 rootEdges=362 rootPositive=2 rootVisitSum=31 rootMaxVisits=28 rootTop=Q16:3:0.102917,D16:28:0.103305
PURE_UDON_GO_CLIENTSIM_RESULT pass=True
```

This was a real serialized UdonSharp/ClientSim run: all five VRC async GPU
readback stages completed, 32 PUCT visits were reported, and `GoGame` accepted
the move. The selected move was D16.

## Fixed external oracle observation

Source log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\katago-initial-32-isolated.log`

The authorized KaTrain executable was run with its fixed configured model and
analysis config, with `homeDataDir` overridden to the allowed Temp directory.
The query is [`queries/initial-32.jsonl`](queries/initial-32.jsonl).

For this one randomized 32-visits query, the first oracle move group was
`R16/R4/C16/C4/D3/Q3/D17/Q17`, each with 10 visits. D16 was in the next tied
group (`Q16/Q4/D16/D4`) with 7 visits, `winrate=0.365411827`, and
`scoreMean=-0.859778682`. The first row was R16 with
`winrate=0.368104832` and `scoreMean=-0.835987302`, giving an observed
absolute difference of `0.002693005` in winrate and `0.02379138` in score mean
for D16 on this query.

This does not prove that D16 is correct for the Udon production model, and it
does not establish a preset ordering, Elo, blunder rate, or game strength. A
future benchmark must use the same oracle model/config consistently, export
more real Udon positions, and include balanced complete games.
