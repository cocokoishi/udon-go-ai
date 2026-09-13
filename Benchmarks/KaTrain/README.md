# KaTrain/KataGo evidence

This directory is reserved for authoritative external-engine comparisons of
the actual Udon ClientSim runtime. It is intentionally empty of benchmark
results until a run records all of the following in the same evidence set:

- UdonSharp transpilation succeeded and produced serialized Udon programs;
- the generated world was executed through ClientSim, not Editor proxy C#;
- real Udon GPU-shader inference and PUCT/MCTS selected the moves;
- the exact matching KataGo/KaTrain oracle evaluated those Udon moves; and
- the result includes reproducible settings, colors, samples, and logs.

All previous Editor C# + temporary SDK-stub reports were retained, but moved
to `Benchmarks/DeprecatedEditorCSharp/`. They must not be cited as Udon,
ClientSim, VRChat, or final-world runtime evidence.
