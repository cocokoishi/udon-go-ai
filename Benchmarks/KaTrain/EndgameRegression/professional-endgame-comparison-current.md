# PureUdonGo professional endgame consistency

Status: `REVIEW_REQUIRED` (position-level screen; not an Elo claim).

Corpus: 32 fixed positions; budgets: 4, 32, 100; samples: 65.

| visits | exact Udon | same-budget Top-1 | candidate | Top-5 | Top-10 | candidate overlap (1/5/10) | mean Jaccard@10 | mean rank (0-based) | mean winrate regret | reference Top-10 | mean reference rank |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | 32/32 | 18/32 | 26/32 | 26/32 | 26/32 | // | 0.615 | 0.462 | 0.002171 | 32/32 | 1.375 |
| 32 | 32/32 | 13/32 | 20/32 | 18/32 | 20/32 | // | 0.427 | 1.100 | 0.009158 | 21/32 | 4.857 |
| 100 | 1/1 | 1/1 | 1/1 | 1/1 | 1/1 | // | 0.333 | 0.000 | 0.000000 | 1/1 | 0.000 |

Overall same-budget Top-1: 32/65. Reference candidate coverage: 61/65; reference Top-10: 54/65.

The same-model oracle and Udon search use the same fixed model/rules query. Candidate coverage and regret are stronger than Top-1 alone at 4/32/100 visits, where deterministic low-budget tie-breaking can differ. This does not establish full policy-distribution identity, bit-for-bit KataGo PUCT identity, Elo, or complete-game win rate.

Machine-readable output: `professional-endgame-comparison-current.json`
