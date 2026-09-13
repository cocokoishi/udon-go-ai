# Udon ClientSim KataGo feature differential after ladder rollback fix

Status: `EQUIVALENCE VERIFIED` for seven generated production feature states.

The real `GoFeatureEncoder` UdonSharp program was executed in ClientSim and
exported `22 x 19 x 19` spatial inputs plus 19 global inputs for each state.
The authorized KaTrain KataGo executable dumped matching INPUTSVERSION 7 NPZ
inputs using the bundled production model and Tromp-Taylor/Chinese control
rules. The comparison tolerance was `1e-6`.

```text
Udon log: E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-feature-rollback-v1.log
Oracle root: E:\UnityPRJS\ShaderGPT_neoversion\Temp\FeatureOracle\2026-09-02-rollback
Report: Benchmarks/KaTrain/udon-clientsim-feature-differential-rollback-2026-09-02.json
PURE_UDON_GO_CLIENTSIM_FEATURE_RESULT passed=True cases=7 ...
PURE_UDON_GO_CLIENTSIM_FEATURE_PASS cases=7 spatialFloats=55594 globalFloats=133
PURE_UDON_GO_KATAGO_FEATURE_ORACLE_PASS cases=7 ...
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=7 failures=0 tolerance=1e-06
```

This covers all 55,594 spatial and 133 global values in the seven-case
corpus, including ladder, history, superko-mask, terminal, and rules-global
channels. It is finite differential coverage; it does not establish
exhaustive/randomized history equivalence or full search-strength calibration.
