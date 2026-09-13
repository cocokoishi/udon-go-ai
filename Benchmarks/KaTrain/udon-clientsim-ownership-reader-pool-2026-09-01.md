# Real Udon ClientSim ownership/readback-pool regression — 2026-09-01

Status: `PASS` for the covered single-VM ClientSim ownership lifecycle.

The production runtime now keeps one pre-generated alternate
`GoGpuNeuralOutputReader` per table. When an in-flight VRC async readback is
invalidated, the old reader remains quarantined so a late callback cannot be
accepted by the new search; the controller switches to the alternate reader.
`GoGame.TakeOwnership()` transfers the alternate reader GameObject together
with the game, AI controller, and telemetry objects.

The real UdonSharp/ClientSim verifier was run with Unity `2022.3.22f1`,
Direct3D 11 (`-force-d3d11`), and the generated production scene. It spawned a
simulated remote player, started a pending GPU readback, transferred the game
and alternate reader ownership, confirmed the old search was cancelled without
an AI move, handed ownership back, and required a fresh 32-visit AI search.

Evidence log:

`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-ownership-d3d11-reader-pool-v3.log`

```text
PURE_UDON_GO_CLIENTSIM_OWNER_PENDING revision=2 moveCount=0 aiMoves=0 ownerLifecycle=2 searchPhase=1 readerState=1 ownerId=1
PURE_UDON_GO_CLIENTSIM_OWNER_REMOTE_PASS ownerId=2 revision=2 ownerLifecycle=4 moveCount=0 aiMoves=0 staleCallbacks=0 cancelled=True searchPhase=4 readerState=4
PURE_UDON_GO_CLIENTSIM_OWNER_PASS remoteOwnerId=2 localOwnerId=1 oldMoveCount=0 finalMoveCount=1 oldAiMoves=0 finalAiMoves=1 visits=32 readerCompletedStages=4 ownershipReadbacks=1 readerRotated=True alternateReaderReturned=True ownerLifecycle=6
PURE_UDON_GO_CLIENTSIM_OWNER_RESULT pass=True
```

The four-stage final readback is expected: ownership is read for the root
telemetry result, while non-root PUCT leaves consume policy/value/score only.
The generated scene and Udon compile were separately revalidated after adding
the reader pool: `generatedRoots=1`, `udonBehaviours=6498`, `programs=45`,
`compiled=45`, and `compilerError=False`.

This is not proof of two independently running VRChat clients or network
transport parity; those still require a real multi-client runtime test.
