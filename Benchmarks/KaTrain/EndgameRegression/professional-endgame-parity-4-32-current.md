# PureUdonGo professional endgame consistency

Status: `REVIEW_REQUIRED` (position-level screen; not an Elo claim).

Corpus: 32 fixed positions; budgets: 4, 32; samples: 64.

| visits | exact Udon | raw visit argmax | play-selection parity | Udon deterministic vs play-selection | sampled Top-1 | deterministic ref rank | sampled ref rank | mean candidate Jaccard@10 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | 32/32 | 28/32 | 20/32 | 20/32 | 18/32 | 0.781 | 1.375 | 0.615 |
| 32 | 32/32 | 29/32 | 25/32 | 25/32 | 13/32 | 0.781 | 4.857 | 0.529 |

Overall raw root-visit argmax parity: 57/64; KataGo play-selection parity: 45/64; Udon deterministic-vs-play-selection Top-1: 45/64. Deterministic reference rank samples: 64; sampled reference rank samples: 60.

The same-model oracle and Udon search use the same fixed model/rules query. Candidate coverage and regret are stronger than Top-1 alone at 4/32 visits, where deterministic low-budget tie-breaking can differ. This does not establish full policy-distribution identity, bit-for-bit KataGo PUCT identity, Elo, or complete-game win rate.

Machine-readable output: `professional-endgame-parity-4-32-current.json`
