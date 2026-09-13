# PureUdonGo professional endgame consistency

Status: `REVIEW_REQUIRED` (position-level screen; not an Elo claim).

Corpus: 32 fixed positions; budgets: 4, 32, 100; samples: 65.

| visits | exact Udon | deterministic Top-1 | sampled Top-1 | deterministic candidate | sampled candidate | deterministic ref rank | sampled ref rank | mean candidate Jaccard@10 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | 32/32 | 18/32 | 18/32 | 26/32 | 26/32 | 1.375 | 1.375 | 0.615 |
| 32 | 32/32 | 13/32 | 13/32 | 20/32 | 20/32 | 4.857 | 4.857 | 0.427 |
| 100 | 1/1 | 1/1 | 1/1 | 1/1 | 1/1 | 0.000 | 0.000 | 0.333 |

Overall deterministic same-budget Top-1: 32/65; sampled Top-1: 32/65. Deterministic reference rank samples: 61; sampled reference rank samples: 61.

The same-model oracle and Udon search use the same fixed model/rules query. Candidate coverage and regret are stronger than Top-1 alone at 4/32/100 visits, where deterministic low-budget tie-breaking can differ. This does not establish full policy-distribution identity, bit-for-bit KataGo PUCT identity, Elo, or complete-game win rate.

Machine-readable output: `professional-endgame-comparison-current-v2.json`
