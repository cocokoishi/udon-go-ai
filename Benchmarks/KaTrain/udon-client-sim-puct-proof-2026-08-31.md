# Real Udon PUCT versus Policy Proof — 2026-08-31

Status: `VERIFIED`; `NOT STRENGTH CALIBRATED`.

The real Unity ClientSim verifier dispatched one Udon event to a compiled
`GoPositionQualityProbe`. The probe configured four fixed positions, enabled
the production Udon AI, and let the normal frame-stepped path perform feature
encoding, GPU Shader inference, five output readback stages, PUCT selection,
and move commit. The editor side only read serialized Udon program variables.

Evidence log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-quality-puct-3.log
```

Invocation:

```text
Unity.exe -batchmode -projectPath E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoRulesCompile -executeMethod PureUdonGo.GoClientSimPositionQualityVerifier.VerifyClientSimPositionQuality
```

## Samples

| Position | Fixed moves | Selected | Root policy prior | Policy agrees | Visits | Nodes | Root edges | Positive root visits | Root visit sum | Readback stages |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| opening | `D16,Q4` | `Q16` | `Q16` | yes | 32 | 32 | 351 | 31 | 31 | 5 |
| capture | `B19,A19,A18` | `Q16` | `D4` | no | 32 | 32 | 351 | 31 | 31 | 5 |
| fight | `K10,L10,K11,K9,J10,M9,J11,M8` | `D16` | `Q4` | no | 32 | 32 | 351 | 31 | 31 | 5 |
| mixed | `D16,Q4,C17,R3,E16,P4,C16,R4,F16,O4,B16` | `C4` | `Q16` | no | 32 | 32 | 351 | 31 | 31 | 5 |

The corresponding runtime lines are the four
`PURE_UDON_GO_CLIENTSIM_QUALITY_SAMPLE` entries in the log. The probe also
recorded `PURE_UDON_GO_CLIENTSIM_QUALITY_PASS scenarios=4 visits=32
policyPuctDivergence=true` and the final result marker was `pass=True`.

## Interpretation

The root prior is computed from the policy logits after legal-move masking;
the selected move is taken from the backed-up root visit ordering. The three
policy/prior divergences and the nontrivial root visit distribution are direct
runtime evidence that the path is not a policy-only argmax shortcut. At 32
visits, root visits remain broad because the 19×19 root has hundreds of legal
actions, so this sample does not claim that value backup has produced a
measurable strength gain. A higher-visit controlled position and balanced
comparisons against the exact authorized KaTrain oracle remain required.
