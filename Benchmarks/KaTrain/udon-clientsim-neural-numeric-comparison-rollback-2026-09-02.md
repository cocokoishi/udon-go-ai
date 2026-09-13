# Udon ClientSim neural numeric comparison after ladder rollback fix

Status: `EQUIVALENCE VERIFIED` for the fixed four-position root corpus.

The source change replaced repeated full `19x19` ladder-board copies with
stamped group/liberty probes and nested per-depth rollback. The nested rollback
repair was required so child ladder mutations cannot leak into a later sibling
branch. The generated Udon program was rebuilt and executed through the real
GPU-backed ClientSim path with Unity 2022.3.22f1, D3D11, and the copied local
license.

## Evidence

Udon export log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-neural-fixed-rollback-v1.log
```

The log contains the real UdonSharp compile and ClientSim markers:

```text
PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_RESULT passed=True cases=4 firstScenario=0 ...
PURE_UDON_GO_CLIENTSIM_NEURAL_OUTPUT_EXPORT_PASS cases=4 visits=32 rootStages=5 numericComparison=not-performed
PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_RESULT pass=True
```

Independent comparator report:

```text
Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-rollback-2026-09-02.json
```

Comparator result:

```text
PURE_UDON_GO_NEURAL_NUMERIC_COMPARISON_PASS cases=4 heads=5 atol=0.0001 rtol=1e-05 tensorExact=True
```

The comparator ran the independent CPU graph over the captured Udon feature
arrays and compared policy spatial, policy pass, value, score, and ownership.
All 127 raw/baked model tensor byte comparisons were exact; all four cases had
zero head mismatches. This is raw-network equivalence for the captured inputs,
not a claim of whole-search or playing-strength equivalence.

## Additional input oracle gate

The same source was run through the real Udon feature probe and then compared
with fresh NPZ inputs dumped by the authorized KaTrain KataGo executable:

```text
Udon log: E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-feature-rollback-v1.log
Oracle root: E:\UnityPRJS\ShaderGPT_neoversion\Temp\FeatureOracle\2026-09-02-rollback
Report: Benchmarks/KaTrain/udon-clientsim-feature-differential-rollback-2026-09-02.json
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=7 failures=0 tolerance=1e-06
```

The randomized root-search stress cases are deliberately not counted here.
The post-fix `random-case-b` input exceeded the single-case 90-second
diagnostic budget in the ladder encoder. `random-case-d` also has no completed
post-fix export (an earlier stress run exceeded the same budget), so both
remain open worst-case scheduling/performance investigations.
