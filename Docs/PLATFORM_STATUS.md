# Platform verification status

Platform claims are evidence-scoped. Do not market an unexecuted platform as
supported merely because the source compiles in another environment.

| platform | current status | required evidence |
| --- | --- | --- |
| Unity 2022.3.22f1 Windows D3D11 + UdonSharp/ClientSim | PASS_HISTORICAL | regenerate and rerun current production schema after latest settings/hover/docs contract changes |
| VRChat Windows desktop / PCVR | OPEN | real client interaction, two-client networking and clean frame-time profiling |
| VRChat Quest / Android | OPEN | actual device/backend build and runtime equivalence evidence |

ClientSim is diagnostic and is not clean VRChat-client frame-time or transport
proof.

The current real-client procedure is defined in [`NETWORKING.md`](NETWORKING.md)
and the authoritative gate state is in [`VERIFICATION.md`](VERIFICATION.md).
There is no separate active legacy two-client checklist.

Quest/Android must remain OPEN until an actual device/backend run succeeds.
Model redistribution authorization is a separate release/legal gate documented
in `THIRD_PARTY_NOTICES.md` and `VERIFICATION.md`.
