# Real Udon ClientSim remote-leave recovery — 2026-09-01

Status: `PASS` for the covered owner-side recovery path.

The real Udon probe prepared three generated Go tables. It used production Udon
actions to create a one-move PvP position on the first two tables, then bound
the test seat/offer fixtures to the actual ClientSim remote player ID. The
third table entered AI-AI mode and received the same remote identity as its AI
control authority. The editor verifier only spawned and removed the remote
ClientSim player; the leave callback and all cleanup observations ran in the
serialized Udon probe.

Unity `2022.3.22f1` ran the generated production scene with Direct3D 11
(`-force-d3d11`). The Udon source compiled together with the temporary probe
(`46` scripts). Evidence log:

`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-network-recovery-v4.log`

```text
PURE_UDON_GO_CLIENTSIM_RECOVERY_REMOTE id=2 name=Go Recovery Witness
PURE_UDON_GO_CLIENTSIM_RECOVERY_EVENT name=RemoveRemotePlayer remoteId=2
PURE_UDON_GO_CLIENTSIM_RECOVERY_RESULT prepared=True probePassed=True undoSeatAfter=-1 drawSeatAfter=-1 undoOfferCleared=True drawOfferCleared=True aiAuthorityAfter=-1 recovery=0->1,0->1,0->1 failure=
PURE_UDON_GO_CLIENTSIM_RECOVERY_PASS
PURE_UDON_GO_CLIENTSIM_RECOVERY_FINAL pass=True
```

The recovery assertions require all of the following: the leaving identity is
observed by Udon, both human seats are cleared, pending PvP undo/draw offers
are cleared, AI-AI authority is cleared, each table increments
`networkRecoveryRevision`, and the two in-progress positions remain playing
with their committed move intact.

The fixture writes synchronized identity fields inside a test-only Udon probe
because ClientSim has one Udon VM and cannot press a visible seat button from a
second independent client. The actual remote-player leave event and the
production `GoGame.OnPlayerLeft` cleanup path are real. Independent VRChat
client transport, remote snapshot delivery, and reconnect with a new player ID
remain unverified.
