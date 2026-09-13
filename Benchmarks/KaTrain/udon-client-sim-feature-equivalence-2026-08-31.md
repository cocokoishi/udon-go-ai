# Real Udon KataGo V7 Feature Differential — 2026-08-31

Status: `EQUIVALENCE VERIFIED`

This report is for the production cooperative Udon encoder, not an Editor C#
reimplementation. The verifier compiled the production UdonSharp scripts with
the real Unity SDK, entered ClientSim, dispatched `RunFeatureProbe` through the
serialized `VRC.Udon.UdonBehaviour`, and exported the arrays from the running
Udon program.

## Evidence

Clean ClientSim/Udon log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-feature-clean.log
```

The log contains:

```text
PURE_UDON_GO_CLIENTSIM_FEATURE_RESULT passed=True cases=7 ... failure=
PURE_UDON_GO_CLIENTSIM_FEATURE_PASS cases=7 spatialFloats=55594 globalFloats=133
PURE_UDON_GO_CLIENTSIM_FEATURE_RESULT pass=True
```

The external comparison used the exact authorized KataGo oracle output in:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\FeatureOracle\2026-08-31-current
```

Command result:

```text
FEATURE_KATAGO_DIFFERENTIAL_PASS cases=7 failures=0 tolerance=1e-06
```

Machine-readable comparison:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\FeatureOracle\2026-08-31-current\differential-clean.json
```

## Corpus

The seven states are `empty-tromp`, `opening-7-tromp`, `fight-15-tromp`,
`ladder-19-tromp`, `capture-9-tromp`, `pass-history-tromp`, and
`double-pass-tromp`. The oracle was generated from the repository fixture by
the repository's `Tools/Features/run_katago_feature_oracle.ps1` script, using
the configured KaTrain KataGo executable/model and deterministic single-thread
settings. The feature comparison covered all 22 spatial planes and 19 global
inputs for every state: 55,594 spatial and 133 global floats, with no mismatch.

## Fixed defect

The capture fixture initially exposed a Udon-only ladder mismatch: planes
14–17 were zero while the KataGo oracle marked the corner ladder groups. The
ordinary synchronous ladder implementation already had the correct target;
the cooperative `libs == 2` branch did not assign `asyncLadderTargetLoc` before
starting its search. It therefore reused a stale/default target in the real
frame-stepped path. Assigning the scanned group start restored the exact
KataGo result. A bounded temporary ClientSim trace confirmed the four scanned
two-liberty groups, results `1,0,1,1`, and two working moves for each positive
target; that trace code was removed before the clean rerun.

This evidence proves feature encoding equivalence for this deterministic corpus.
It does not by itself prove full neural-output equivalence, shader numerical
equivalence, search strength, networking, or VRChat/device runtime.
