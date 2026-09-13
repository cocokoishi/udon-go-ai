# Pure Udon Go — third-party notices

This document records provenance that must travel with a public world release.
It is an evidence checklist, not a substitute for the license text of any
dependency.

## KataGo source

The bundled reference source is under `.KataGO/KataGo-master/`. Its license
and the notices for vendored dependencies are kept in:

```text
.KataGO/KataGo-master/LICENSE
.KataGO/KataGo-master/cpp/external/
```

Upstream project name and URL, as recorded by that bundled license:

```text
KataGo
https://github.com/lightvector/KataGo
Copyright 2025 David J Wu ("lightvector") and/or other contributors
License: MIT-style terms in .KataGO/KataGo-master/LICENSE
```

The KataGo source is an editor/oracle dependency. It is not a runtime
requirement of the generated VRChat world.

## Production neural weights

The production model is identified as:

```text
file: .KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz
model name: g170e-b10c128-s1141046784-d204142634
SHA256: 1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04
```

The generated runtime tensor provenance is recorded in
`Assets/PureUdonGo/Model/Generated/ModelManifest.json`. The checkout does not
contain a source URL or a license/redistribution grant specifically tied to
this model file. The model's redistribution rights are therefore deliberately
not inferred from the KataGo source-code license. Before public upload, record
the model's exact download URL and retain its applicable redistribution terms.
This is an **OPEN release gate**, not a support or license claim.

## Fonts

The bundled CJK font notice is:

```text
Assets/PureUdonGo/Fonts/Noto-CJK-LICENSE.txt
```

It contains the applicable SIL Open Font License 1.1 text and attribution.

## Unity, VRChat SDK and UdonSharp

Unity, VRChat SDK/Udon and UdonSharp are project/toolchain dependencies. Their
licenses and distribution terms remain those supplied by the installed
toolchain; this repository does not relicense them.
