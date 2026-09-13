# PureUdonGo professional endgame consistency

Status: `REVIEW_REQUIRED` (position-level screen; not an Elo claim).

Corpus: 32 fixed positions; budgets: 8, 48; samples: 64.

| visits | exact Udon | raw visit argmax | play-selection parity | Udon deterministic vs play-selection | sampled Top-1 | deterministic ref rank | sampled ref rank | mean candidate Jaccard@10 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 8 | 32/32 | 29/32 | 22/32 | 22/32 | 16/32 | 0.812 | 2.031 | 0.467 |
| 48 | 32/32 | 25/32 | 21/32 | 21/32 | 19/32 | 0.781 | 1.500 | 0.536 |

Overall raw root-visit argmax parity: 54/64; KataGo play-selection parity: 43/64; Udon deterministic-vs-play-selection Top-1: 43/64. Deterministic reference rank samples: 64; sampled reference rank samples: 60.

The same-model oracle and Udon search use the same fixed model/rules query. Candidate coverage and regret are stronger than Top-1 alone at 4/32 visits, where deterministic low-budget tie-breaking can differ. This does not establish full policy-distribution identity, bit-for-bit KataGo PUCT identity, Elo, or complete-game win rate.

Machine-readable output: `professional-endgame-comparison-8-48-current-rerun.json`
