# Real Udon ClientSim ownership/revision regression — 2026-08-31

Status: `VERIFIED` for the generated Udon ownership and lifecycle path with a
simulated remote player; `NOT RUNTIME VERIFIED` for two independently running
VRChat clients.

Compile/generator gate:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-40.log`

Runtime log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-owner-2.log`

The verifier used the real ClientSim API to spawn one remote player, then sent
the normal Udon start events. It waited for a pending GPU request before using
the ClientSim-backed `Networking.SetOwner` path to move the generated Go game
object from the local player to the remote player, and finally handed it back.
The verifier only inspected serialized Udon variables; it did not call Go
runtime methods directly from the editor.

Observed evidence:

```text
PURE_UDON_GO_CLIENTSIM_OWNER_PLAYERS count=2 localId=1 remoteId=2 initialOwnerId=1 isMaster=True
PURE_UDON_GO_CLIENTSIM_OWNER_PENDING revision=2 moveCount=0 aiMoves=0 ownerLifecycle=2 searchPhase=1 readerState=1 ownerId=1
PURE_UDON_GO_CLIENTSIM_OWNER_EVENT name=SetOwnerRemote remoteId=2
PURE_UDON_GO_CLIENTSIM_OWNER_TRANSFER remoteOwnerId=2 ownerLifecycleBefore=2 ownerLifecycleAfter=4
PURE_UDON_GO_CLIENTSIM_OWNER_REMOTE_PASS ownerId=2 revision=2 ownerLifecycle=4 moveCount=0 aiMoves=0 staleCallbacks=0 cancelled=True searchPhase=4 readerState=4
PURE_UDON_GO_CLIENTSIM_OWNER_TRANSFER localOwnerId=1 ownerLifecycle=6
PURE_UDON_GO_CLIENTSIM_OWNER_PASS remoteOwnerId=2 localOwnerId=1 oldMoveCount=0 finalMoveCount=1 oldAiMoves=0 finalAiMoves=1 visits=32 readerCompletedStages=5 ownerLifecycle=6
PURE_UDON_GO_CLIENTSIM_OWNER_RESULT pass=True
```

This proves the old local authority cannot commit the in-flight result after
ownership moves away, the Udon owner lifecycle changes, and the returned local
authority can resume a fresh GPU search and commit a move. ClientSim cancels
the pending readback without delivering a late callback in this scenario, so
`staleCallbacks=0` is intentional; the separate reset verifier covers a real
late callback with `staleCallbacks=1`. A real VRChat multi-client/network
transport test is still required before claiming network runtime completion.
