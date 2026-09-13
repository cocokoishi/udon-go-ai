# 4/32 deterministic parity index

This report separates raw root-visit argmax from KataGo play-selection ordering.

| position | 4v Udon | 4v KataGo play | 4v raw set | 32v Udon | 32v KataGo play | 32v raw set | category |
| --- | --- | --- | --- | --- | --- | --- | --- |
| go-seigen-1966-01-05 | C14 | A10 | MATCH | C14 | C14 | MATCH | D_RAW_MATCH_BOTH |
| go-seigen-1973-07-01 | R5 | R5 | MATCH | R5 | R5 | MATCH | D_RAW_MATCH_BOTH |
| go-seigen-1951-04-11 | H7 | H7 | MATCH | H7 | H7 | MATCH | D_RAW_MATCH_BOTH |
| go-seigen-1971-09-12 | L8 | L8 | MATCH | L8 | L8 | MATCH | D_RAW_MATCH_BOTH |
| cho-chikun-2021-01-07 | B1 | F1 | MATCH | B1 | F1 | MISMATCH | C_ONLY_32_RAW_MISMATCH |
| cho-chikun-2018-10-22 | E4 | E4 | MATCH | E4 | E8 | MATCH | D_RAW_MATCH_BOTH |
| cho-chikun-2025-05-22 | A13 | A13 | MATCH | A13 | A13 | MATCH | D_RAW_MATCH_BOTH |
| cho-chikun-2012-04-12b | A19 | A19 | MATCH | A19 | A19 | MATCH | D_RAW_MATCH_BOTH |
| alphago-fanhui-1 | Q13 | C16 | MATCH | Q13 | Q13 | MATCH | D_RAW_MATCH_BOTH |
| alphago-fanhui-2 | H15 | P13 | MATCH | H15 | H15 | MATCH | D_RAW_MATCH_BOTH |
| alphago-fanhui-5 | J17 | J17 | MATCH | J17 | J17 | MATCH | D_RAW_MATCH_BOTH |
| alphago-lee-sedol-1 | B3 | B3 | MATCH | B3 | B3 | MATCH | D_RAW_MATCH_BOTH |
| alphago-lee-sedol-2 | K6 | K6 | MATCH | K6 | K6 | MATCH | D_RAW_MATCH_BOTH |
| alphago-lee-sedol-5 | M5 | A7 | MATCH | M5 | A7 | MATCH | D_RAW_MATCH_BOTH |
| alphago-ke-jie-1 | A6 | A6 | MATCH | A6 | A6 | MATCH | D_RAW_MATCH_BOTH |
| alphago-ke-jie-2 | D4 | D4 | MATCH | D4 | D4 | MATCH | D_RAW_MATCH_BOTH |
| master-mi-yuting | R2 | R2 | MATCH | R2 | R2 | MATCH | D_RAW_MATCH_BOTH |
| master-jiang-weijie | C3 | D19 | MATCH | C3 | C3 | MATCH | D_RAW_MATCH_BOTH |
| master-chen-yaoye-t22 | H16 | H16 | MATCH | H16 | H16 | MATCH | D_RAW_MATCH_BOTH |
| master-meng-tailing-t09 | G1 | G1 | MATCH | G1 | G1 | MATCH | D_RAW_MATCH_BOTH |
| master-meng-tailing-f40 | R15 | E19 | MATCH | R15 | R15 | MATCH | D_RAW_MATCH_BOTH |
| master-chen-yaoye-t21 | A13 | A13 | MATCH | A13 | A13 | MATCH | D_RAW_MATCH_BOTH |
| master-park-junghwan-f48 | H11 | M10 | MISMATCH | H11 | M10 | MATCH | B_ONLY_4_RAW_MISMATCH |
| master-chen-yaoye-f55 | J12 | J12 | MATCH | J12 | J12 | MATCH | D_RAW_MATCH_BOTH |
| master-park-junghwan-t25 | D8 | G4 | MISMATCH | D8 | G4 | MISMATCH | A_BOTH_RAW_MISMATCH |
| master-an-sungjun | T12 | E19 | MATCH | T12 | E19 | MATCH | D_RAW_MATCH_BOTH |
| master-park-junghwan-t20 | H1 | H1 | MATCH | H1 | H1 | MATCH | D_RAW_MATCH_BOTH |
| master-nie-weiping | D17 | L13 | MISMATCH | D17 | L13 | MISMATCH | A_BOTH_RAW_MISMATCH |
| master-tuo-jiaxi | O14 | O14 | MATCH | O14 | O14 | MATCH | D_RAW_MATCH_BOTH |
| master-gu-li | M1 | L15 | MISMATCH | M1 | M1 | MATCH | B_ONLY_4_RAW_MISMATCH |
| master-ke-jie-t18 | L13 | L13 | MATCH | L13 | L13 | MATCH | D_RAW_MATCH_BOTH |
| master-park-junghwan-t24 | A2 | A2 | MATCH | A2 | A2 | MATCH | D_RAW_MATCH_BOTH |

4v raw-visit set mismatches: 4; 32v: 3.

Raw visit argmax is compared as a tie-aware set. KataGo play-selection and sampled product moves remain separate metrics. The current report does not claim exact first divergence until a per-playout KataGo hook is available.
