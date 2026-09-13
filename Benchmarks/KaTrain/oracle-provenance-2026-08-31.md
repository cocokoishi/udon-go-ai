# KaTrain/KataGo oracle provenance — 2026-08-31

`VERIFIED` for direct execution of the authorized external development oracle.
This file is not a claim about the shipped Udon runtime or preset strength.

## Fixed provenance

- KaTrain root: `E:\UnityPRJS\ShaderGPT_neoversion\KaTrain`
- KataGo executable: `_internal/katrain/KataGo/katago.exe`
- KataGo version: `v1.18.1`
- KataGo Git revision: `92ee95c0a4b25fec214da00951ab69e97e207729`
- Backend/device: OpenCL · NVIDIA GeForce RTX 3060 Laptop GPU
- Model: `_internal/katrain/models/b10c384h6nbttflrs.bin.gz`
- Model name/architecture: `b10c384h6nbttflrs` · nbt transformer · 10,545,753 parameters
- KaTrain config: `_internal/katrain/KataGo/analysis_config.cfg`
- Config: `maxVisits=500`, `numAnalysisThreads=12`, `numSearchThreads=8`, `nnMaxBatchSize=96`, `reportAnalysisWinratesAs=BLACK`, `nnRandomize=true`
- KataGo executable SHA-256: `0E6CC918840E6EF67C0B4A21109585306DF7AAA3772971574E58DEB8E8FC0575`
- Model SHA-256: `0BA27ECED5180B3E3D0B898B280C541112989765E789D1EB6CD0D31B2B2C1229`
- Analysis config SHA-256: `FAF458DD0BD7923DB48C1043AF88F6CDFB938EBAF5950B116A82CA2CD4DC80C5`

The command used the read-only executable/model/config from KaTrain and passed
`-override-config homeDataDir=E:\UnityPRJS\ShaderGPT_neoversion\Temp\KaTrainOracle`.
The override kept OpenCL tuning/cache writes in the allowed Temp tree. The
raw transient output is at
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\katago-initial-32-isolated.log`.

## Initial-position query

Input: [`queries/initial-32.jsonl`](queries/initial-32.jsonl), SHA-256
`FF73ED5CD3661CE3BF2DCDF168790BF03F4D099B03EB5095C378DBB4B51F4A1A`.

The query used a 19×19 empty board, Chinese rules, komi 7.5, policy and
ownership output enabled, and `maxVisits=32`. The returned JSON reported
`rootInfo.visits=35` (the engine's parallel analysis result) and the following
top move distribution:

| Rank group | Moves | Visits | Prior | Winrate | Score mean |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | R16, R4, C16, C4, D3, Q3, D17, Q17 | 10 each | 0.0779693 | 0.368105 | -0.83599 |
| 2 | Q16, Q4, D16, D4 | 7 each | 0.0517059 | 0.365412 | -0.85978 |

Root values were `winrate=0.366995294`, `scoreLead=-0.850848658`, and
`utility=-0.267352146`. This demonstrates the external engine and query
pipeline are live and records a reference distribution for later Udon move
quality comparison. No Udon move is compared here because the current
ClientSim smoke log does not yet export the selected coordinate; that remains
an open benchmark task.
