#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Exercises the real ClientSim remote-player ownership path. ClientSim
    /// provides one local Udon VM plus a simulated remote player, so this is
    /// explicitly an ownership/revision simulation rather than a claim about
    /// two independently running VRChat clients.
    /// </summary>
    public static class GoClientSimOwnershipVerifier
    {
        private const double TimeoutSeconds = 240.0;
        private const double TransferSettleSeconds = 30.0;
        private const int ReadbackWaiting = 1;
        private const int ReadbackCancelled = 4;
        private const int ReadbackQuarantined = 5;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimOwnershipVerifier.Pending";
        private const string StepSessionKey =
            "PureUdonGo.ClientSimOwnershipVerifier.Step";

        private static double deadline;
        private static double settleDeadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool remoteSpawnRequested;
        private static bool activeReaderRebound;
        private static int step;
        private static int baselineRevision;
        private static int baselineMoveCount;
        private static int baselineAiMoves;
        private static int lifecycleBeforeTransfer;
        private static int staleBeforeTransfer;
        private static int recoveredBeforeTransfer;
        private static int ownershipBeforeTransfer;
        private static int lifecycleAfterRemote;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;
        private static UdonBehaviour initialReaderProgram;
        private static UdonBehaviour alternateReaderProgram;
        private static UdonBehaviour tertiaryReaderProgram;
        private static VRCPlayerApi localPlayer;
        private static VRCPlayerApi remotePlayer;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false))
                return;

            step = SessionState.GetInt(StepSessionKey, 0);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_REATTACHED step=" + step);
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Ownership And Revision")]
        public static void VerifyClientSimOwnershipAndRevision()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " +
                    Editor.GoWorldGenerator.ScenePath);

            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim = true,
                displayLogs = true,
                deleteEditorOnly = false,
                spawnPlayer = true,
                hideMenuOnLaunch = true,
                setTargetFrameRate = false,
                localPlayerIsMaster = true,
                isInstanceOwner = true,
                initializationDelay = 0f,
                currentLanguage = "en"
            });

            SessionState.SetInt(StepSessionKey, 0);
            SessionState.SetBool(PendingSessionKey, true);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_START scene=" +
                Editor.GoWorldGenerator.ScenePath +
                " timeoutSeconds=" + TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            settleDeadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            remoteSpawnRequested = false;
            activeReaderRebound = false;
            step = 0;
            baselineRevision = 0;
            baselineMoveCount = 0;
            baselineAiMoves = 0;
            lifecycleBeforeTransfer = 0;
            staleBeforeTransfer = 0;
            recoveredBeforeTransfer = 0;
            ownershipBeforeTransfer = 0;
            lifecycleAfterRemote = 0;
            gameProgram = null;
            aiProgram = null;
            searchProgram = null;
            readerProgram = null;
            initialReaderProgram = null;
            alternateReaderProgram = null;
            tertiaryReaderProgram = null;
            localPlayer = null;
            remotePlayer = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before ownership regression completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }

                ResolvePrograms();
                if (gameProgram == null || aiProgram == null ||
                    searchProgram == null || readerProgram == null)
                {
                    FailIfTimedOut("Generated Go Udon programs were not all found");
                    return;
                }

                localPlayer = Networking.LocalPlayer;
                if (step == 0)
                {
                    if (!remoteSpawnRequested)
                    {
                        ClientSimMain.SpawnRemotePlayer("Remote Go Owner");
                        remoteSpawnRequested = true;
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_EVENT name=SpawnRemotePlayer");
                    }
                    SetStep(1);
                    return;
                }

                if (step == 1)
                {
                    remotePlayer = FindRemotePlayer();
                    if (remotePlayer == null || localPlayer == null)
                    {
                        FailIfTimedOut("ClientSim remote player was not spawned");
                        return;
                    }
                    VRCPlayerApi initialOwner = Networking.GetOwner(gameProgram.gameObject);
                    if (initialOwner == null || initialOwner.playerId != localPlayer.playerId)
                    {
                        FailIfTimedOut("Go game did not start with local owner");
                        return;
                    }
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_PLAYERS count=" +
                        VRCPlayerApi.AllPlayers.Count + " localId=" + localPlayer.playerId +
                        " remoteId=" + remotePlayer.playerId + " initialOwnerId=" +
                        initialOwner.playerId + " isMaster=" + Networking.IsMaster);
                    gameProgram.SendCustomEvent("SetWhiteStarts");
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_EVENT name=SetWhiteStarts");
                    SetStep(2);
                    return;
                }

                if (step == 2)
                {
                    if (ReadInt(gameProgram, "starter") != GoGame.WHITE ||
                        ReadInt(gameProgram, "sideToMove") != GoGame.WHITE)
                        return;
                    gameProgram.SendCustomEvent("RequestStartMatch");
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_EVENT name=RequestStartMatch");
                    SetStep(3);
                    return;
                }

                if (step == 3)
                {
                    int readerState = ReadInt(readerProgram, "readbackState");
                    int phase = ReadInt(searchProgram, "phase");
                    if (readerState != ReadbackWaiting || phase != GoMctsSearch.PHASE_WAITING_NEURAL)
                        return;
                    baselineRevision = ReadInt(gameProgram, "revision");
                    baselineMoveCount = ReadInt(gameProgram, "moveCount");
                    baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                    lifecycleBeforeTransfer = ReadInt(aiProgram, "ownerLifecycle");
                    staleBeforeTransfer = ReadInt(searchProgram, "staleResultsDiscarded");
                    ownershipBeforeTransfer = ReadInt(readerProgram, "ownershipReadbackCount");
                    recoveredBeforeTransfer = ReadInt(readerProgram, "recoveredCallbacks");
                    initialReaderProgram = readerProgram;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_PENDING revision=" +
                        baselineRevision + " moveCount=" + baselineMoveCount +
                        " aiMoves=" + baselineAiMoves + " ownerLifecycle=" +
                        lifecycleBeforeTransfer + " searchPhase=" + phase +
                        " readerState=" + readerState + " ownerId=" +
                        Networking.GetOwner(gameProgram.gameObject).playerId);
                    Networking.SetOwner(remotePlayer, gameProgram.gameObject);
                    if (alternateReaderProgram != null)
                        Networking.SetOwner(remotePlayer, alternateReaderProgram.gameObject);
                    if (tertiaryReaderProgram != null)
                        Networking.SetOwner(remotePlayer, tertiaryReaderProgram.gameObject);
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_EVENT name=SetOwnerRemote remoteId=" +
                        remotePlayer.playerId);
                    SetStep(4);
                    return;
                }

                if (step == 4)
                {
                    VRCPlayerApi owner = Networking.GetOwner(gameProgram.gameObject);
                    if (owner == null || owner.playerId != remotePlayer.playerId)
                    {
                        FailIfTimedOut("remote ownership transfer did not apply");
                        return;
                    }
                    if (alternateReaderProgram != null &&
                        OwnerId(alternateReaderProgram) != remotePlayer.playerId)
                    {
                        FailIfTimedOut("alternate reader ownership transfer did not apply");
                        return;
                    }
                    if (tertiaryReaderProgram != null &&
                        OwnerId(tertiaryReaderProgram) != remotePlayer.playerId)
                    {
                        FailIfTimedOut("tertiary reader ownership transfer did not apply");
                        return;
                    }
                    lifecycleAfterRemote = ReadInt(aiProgram, "ownerLifecycle");
                    if (lifecycleAfterRemote <= lifecycleBeforeTransfer)
                        return;
                    settleDeadline = EditorApplication.timeSinceStartup + TransferSettleSeconds;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_TRANSFER remoteOwnerId=" +
                        owner.playerId + " ownerLifecycleBefore=" + lifecycleBeforeTransfer +
                        " ownerLifecycleAfter=" + lifecycleAfterRemote);
                    SetStep(5);
                    return;
                }

                if (step == 5)
                {
                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int aiMoves = ReadInt(aiProgram, "aiMoves");
                    int readerState = ReadInt(readerProgram, "readbackState");
                    int phase = ReadInt(searchProgram, "phase");
                    int stale = ReadInt(searchProgram, "staleResultsDiscarded");
                    if (EditorApplication.timeSinceStartup < settleDeadline)
                        return;
                    if (moveCount != baselineMoveCount || aiMoves != baselineAiMoves)
                    {
                        Fail("old local owner committed after remote transfer moveCount=" +
                            moveCount + " aiMoves=" + aiMoves);
                        return;
                    }
                    if (readerState == ReadbackWaiting || phase == GoMctsSearch.PHASE_WAITING_NEURAL ||
                        phase == GoMctsSearch.PHASE_EXPANDING || phase == GoMctsSearch.PHASE_SELECTING ||
                        phase == GoMctsSearch.PHASE_BACKING_UP ||
                        phase == GoMctsSearch.PHASE_RUNNING)
                    {
                        Fail("old search remained active after remote transfer phase=" + phase +
                            " readerState=" + readerState);
                        return;
                    }
                    int recovered = ReadInt(readerProgram, "recoveredCallbacks");
                    bool callbackObserved = stale > staleBeforeTransfer ||
                        readerState == ReadbackCancelled || readerState == ReadbackQuarantined ||
                        (readerState == GoGpuNeuralOutputReader.READBACK_RECOVERED &&
                        recovered > recoveredBeforeTransfer);
                    if (!callbackObserved)
                    {
                        Fail("remote transfer did not observe stale/cancelled callback staleBefore=" +
                            staleBeforeTransfer + " staleAfter=" + stale +
                            " readerState=" + readerState + " recoveredBefore=" +
                            recoveredBeforeTransfer + " recoveredAfter=" + recovered);
                        return;
                    }
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_REMOTE_PASS ownerId=" +
                        remotePlayer.playerId + " revision=" + ReadInt(gameProgram, "revision") +
                        " ownerLifecycle=" + lifecycleAfterRemote + " moveCount=" + moveCount +
                        " aiMoves=" + aiMoves + " staleCallbacks=" + stale +
                        " cancelled=" + (readerState == ReadbackCancelled ||
                            readerState == ReadbackQuarantined) +
                        " searchPhase=" + phase + " readerState=" + readerState);
                    Networking.SetOwner(localPlayer, gameProgram.gameObject);
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_EVENT name=SetOwnerLocal localId=" +
                        localPlayer.playerId);
                    SetStep(6);
                    return;
                }

                if (step == 6)
                {
                    VRCPlayerApi owner = Networking.GetOwner(gameProgram.gameObject);
                    if (owner == null || owner.playerId != localPlayer.playerId)
                    {
                        FailIfTimedOut("local ownership handback did not apply");
                        return;
                    }
                    if (alternateReaderProgram != null &&
                        OwnerId(alternateReaderProgram) != localPlayer.playerId)
                        return;
                    if (tertiaryReaderProgram != null &&
                        OwnerId(tertiaryReaderProgram) != localPlayer.playerId)
                        return;
                    if (ReadInt(aiProgram, "ownerLifecycle") <= lifecycleAfterRemote)
                        return;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_TRANSFER localOwnerId=" +
                        owner.playerId + " ownerLifecycle=" + ReadInt(aiProgram, "ownerLifecycle"));
                    SetStep(7);
                    return;
                }

                if (step == 7)
                {
                    if (!activeReaderRebound)
                    {
                        ResolveActiveReaderProgram();
                        activeReaderRebound = true;
                    }
                    int phase = ReadInt(searchProgram, "phase");
                    int readerState = ReadInt(readerProgram, "readbackState");
                    if (phase == GoMctsSearch.PHASE_WAITING_NEURAL ||
                        phase == GoMctsSearch.PHASE_EXPANDING || phase == GoMctsSearch.PHASE_SELECTING ||
                        phase == GoMctsSearch.PHASE_BACKING_UP ||
                        phase == GoMctsSearch.PHASE_RUNNING ||
                        readerState == ReadbackWaiting)
                    {
                        SetStep(8);
                    }
                    FailIfTimedOut("new local owner did not resume AI search");
                    return;
                }

                if (step == 8)
                {
                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int aiMoves = ReadInt(aiProgram, "aiMoves");
                    int visits = ReadInt(searchProgram, "visitsCompleted");
                    int stages = ReadInt(readerProgram, "completedStages");
                    int ownershipReadbacks = ReadInt(readerProgram, "ownershipReadbackCount");
                    int controllerState = ReadInt(aiProgram, "controllerState");
                    bool readerRotated = initialReaderProgram != null &&
                        readerProgram != null && initialReaderProgram != readerProgram;
                    bool alternateReaderReturned = alternateReaderProgram == null ||
                        OwnerId(alternateReaderProgram) == localPlayer.playerId;
                    bool tertiaryReaderReturned = tertiaryReaderProgram == null ||
                        OwnerId(tertiaryReaderProgram) == localPlayer.playerId;
                    if (controllerState == GoAiController.STATE_ERROR)
                    {
                        Fail("new local owner AI error=" + ReadString(aiProgram, "lastError"));
                        return;
                    }
                    if (moveCount > baselineMoveCount && aiMoves > baselineAiMoves &&
                        visits >= GoDifficultyProfile.BEGINNER_VISITS && stages == 1 &&
                        (readerRotated || ownershipReadbacks > ownershipBeforeTransfer) &&
                        alternateReaderReturned && tertiaryReaderReturned)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_PASS remoteOwnerId=" +
                            remotePlayer.playerId + " localOwnerId=" + localPlayer.playerId +
                            " oldMoveCount=" + baselineMoveCount + " finalMoveCount=" + moveCount +
                            " oldAiMoves=" + baselineAiMoves + " finalAiMoves=" + aiMoves +
                             " visits=" + visits + " readerCompletedStages=" + stages +
                             " ownershipReadbacks=" + ownershipReadbacks +
                             " readerRotated=" + readerRotated +
                             " alternateReaderReturned=" + alternateReaderReturned +
                             " tertiaryReaderReturned=" + tertiaryReaderReturned +
                             " ownerLifecycle=" + ReadInt(aiProgram, "ownerLifecycle"));
                        StopWithResult(true);
                        return;
                    }
                    FailIfTimedOut("new local owner did not commit a fresh AI move" +
                        " moveCount=" + moveCount + " aiMoves=" + aiMoves +
                        " visits=" + visits + " stages=" + stages +
                        " ownershipReadbacks=" + ownershipReadbacks +
                        " ownershipBeforeTransfer=" + ownershipBeforeTransfer +
                        " gameOwner=" + OwnerId(gameProgram) +
                        " aiObjectOwner=" + OwnerId(aiProgram) +
                        " matchStarted=" + ReadBool(gameProgram, "matchStarted") +
                        " gameState=" + ReadInt(gameProgram, "gameState") +
                        " sideToMove=" + ReadInt(gameProgram, "sideToMove") +
                        " blackIsAI=" + ReadBool(gameProgram, "blackIsAI") +
                        " whiteIsAI=" + ReadBool(gameProgram, "whiteIsAI") +
                        " autoStart=" + ReadBool(aiProgram, "autoStart") +
                        " schedulerEnabled=" + ReadBool(aiProgram, "schedulerEnabled") +
                        " controllerState=" + ReadInt(aiProgram, "controllerState") +
                        " lastError=" + ReadString(aiProgram, "lastError"));
                    return;
                }

                Fail("unknown ownership verifier state=" + step);
            }
            catch (Exception exception)
            {
                Fail("ClientSim ownership inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            if (gameProgram == null)
            {
                GoGame gameProxy = UnityEngine.Object.FindObjectOfType<GoGame>();
                if (gameProxy != null)
                    gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy);
            }
            if (aiProgram == null)
            {
                GoAiController aiProxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
                if (aiProxy != null)
                {
                    aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy);
                    searchProgram = aiProxy.search == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.search);
                    readerProgram = aiProxy.reader == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.reader);
                    tertiaryReaderProgram = aiProxy.tertiaryReader == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.tertiaryReader);
                }
            }
            if (aiProgram != null)
            {
                if (readerProgram == null)
                    readerProgram = ReadReference(aiProgram, "reader");
                if (alternateReaderProgram == null)
                    alternateReaderProgram = ReadReference(aiProgram, "alternateReader");
                if (tertiaryReaderProgram == null)
                    tertiaryReaderProgram = ReadReference(aiProgram, "tertiaryReader");
            }
        }

        private static void ResolveActiveReaderProgram()
        {
            if (aiProgram == null)
                return;
            UdonBehaviour activeReader = aiProgram.GetProgramVariable("reader") as UdonBehaviour;
            if (activeReader != null)
                readerProgram = activeReader;
        }

        private static UdonBehaviour ReadReference(UdonBehaviour program, string variableName)
        {
            return program == null ? null :
                program.GetProgramVariable(variableName) as UdonBehaviour;
        }

        private static VRCPlayerApi FindRemotePlayer()
        {
            for (int i = 0; i < VRCPlayerApi.AllPlayers.Count; i++)
            {
                VRCPlayerApi player = VRCPlayerApi.AllPlayers[i];
                if (player != null && !player.isLocal)
                    return player;
            }
            return null;
        }

        private static int ReadInt(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static bool ReadBool(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static string ReadString(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value == null ? "" : value.ToString();
        }

        private static int OwnerId(UdonBehaviour program)
        {
            if (program == null || program.gameObject == null)
                return -1;
            VRCPlayerApi owner = Networking.GetOwner(program.gameObject);
            return owner == null ? -1 : owner.playerId;
        }

        private static void SetStep(int value)
        {
            step = value;
            SessionState.SetInt(StepSessionKey, value);
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup < deadline)
                return;
            Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten)
                return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_OWNER_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_OWNER_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
