#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Exercises two consecutive unresolved readback cancellations in the
    /// real serialized Udon path. ClientSim may suppress the cancelled
    /// callback, which makes this a useful deterministic liveness probe for
    /// the fixed A/B/C reader pool. It does not claim real VRChat transport.
    /// </summary>
    public static class GoClientSimReaderPoolVerifier
    {
        private const double TimeoutSeconds = 240.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimReaderPoolVerifier.Pending";
        private const string StepSessionKey =
            "PureUdonGo.ClientSimReaderPoolVerifier.Step";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int step;
        private static int baselineAiMoves;
        private static int baselineMoveCount;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerAProgram;
        private static UdonBehaviour readerBProgram;
        private static UdonBehaviour readerCProgram;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_REATTACHED step=" + step);
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Three-Reader Quarantine")]
        public static void VerifyClientSimThreeReaderQuarantine()
        {
            ResetRunner();
            Editor.GoWorldGenerator.GenerateReaderRecoveryFixtureScene();
            Scene sourceScene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ReaderRecoveryScenePath, OpenSceneMode.Single);
            if (!sourceScene.IsValid() || !sourceScene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " +
                    Editor.GoWorldGenerator.ReaderRecoveryScenePath);
            GoGame fixtureGame=UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController fixtureController=
                UnityEngine.Object.FindObjectOfType<GoAiController>();
            if(fixtureGame==null||fixtureController==null||
                UnityEngine.Object.FindObjectsOfType<GoGpuNeuralOutputReader>().Length!=3||
                UnityEngine.Object.FindObjectOfType<GoBoardPool>()!=null||
                UnityEngine.Object.FindObjectOfType<GoBoardCell>()!=null||
                UnityEngine.Object.FindObjectOfType<Canvas>()!=null)
                throw new InvalidOperationException("Minimal reader fixture runtime is incomplete");

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

            SessionState.SetBool(PendingSessionKey, true);
            SessionState.SetInt(StepSessionKey, 0);
            EditorSceneManager.SetActiveScene(sourceScene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_START scene=" +
                Editor.GoWorldGenerator.ReaderRecoveryScenePath + " timeoutSeconds=" +
                TimeoutSeconds+" games=1 readers=3");
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            step = 0;
            baselineAiMoves = 0;
            baselineMoveCount = 0;
            gameProgram = null;
            aiProgram = null;
            searchProgram = null;
            readerAProgram = null;
            readerBProgram = null;
            readerCProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before three-reader verification completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_PLAYMODE_ENTERED");
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
                    searchProgram == null || readerAProgram == null ||
                    readerBProgram == null || readerCProgram == null)
                {
                    FailIfTimedOut("Generated Go A/B/C reader programs were not all found");
                    return;
                }

                if (step == 0)
                {
                    baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                    baselineMoveCount = ReadInt(gameProgram, "moveCount");
                    SendEvent(gameProgram, "SetWhiteStarts", "prepare white AI turn");
                    SendEvent(gameProgram, "RequestStartMatch", "start PvAI match");
                    SetStep(1);
                    return;
                }

                int phase = ReadInt(searchProgram, "phase");
                int readerAState = ReadInt(readerAProgram, "readbackState");
                int readerBState = ReadInt(readerBProgram, "readbackState");
                int readerCState = ReadInt(readerCProgram, "readbackState");

                if (step == 1)
                {
                    if (readerAState != GoGpuNeuralOutputReader.READBACK_WAITING ||
                        phase != GoMctsSearch.PHASE_WAITING_NEURAL)
                    {
                        FailIfTimedOut("reader A did not enter a pending root readback");
                        return;
                    }

                    SendEvent(aiProgram, "InvalidateSearch", "cancel reader A and rotate to B");
                    SetStep(2);
                    return;
                }

                if (step == 2)
                {
                    // ClientSim can deliver the cancelled callback in the
                    // same frame as InvalidateSearch. In that valid ordering
                    // A is already RECOVERED; accept it as equivalent to an
                    // observable quarantine while still requiring a
                    // recovered callback counter.
                    int aStateNow=ReadInt(readerAProgram, "readbackState");
                    bool aQuarantined = (ReadBool(readerAProgram, "quarantinePending") &&
                        aStateNow==GoGpuNeuralOutputReader.READBACK_QUARANTINED) ||
                        (aStateNow==GoGpuNeuralOutputReader.READBACK_RECOVERED &&
                        ReadInt(readerAProgram, "recoveredCallbacks")>0);
                    if (!aQuarantined || readerBState != GoGpuNeuralOutputReader.READBACK_WAITING)
                    {
                        FailIfTimedOut("reader B did not start while reader A remained quarantined");
                        return;
                    }

                    Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_FIRST_QUARANTINE aState=" +
                        ReadInt(readerAProgram, "readbackState") + " bState=" + readerBState +
                        " rotations=" + ReadInt(aiProgram, "readerRotations"));
                    SendEvent(aiProgram, "InvalidateSearch", "cancel reader B and rotate to C");
                    SetStep(3);
                    return;
                }

                if (step == 3)
                {
                    int aStateNow=ReadInt(readerAProgram, "readbackState");
                    int bStateNow=ReadInt(readerBProgram, "readbackState");
                    bool aQuarantined = ReadBool(readerAProgram, "quarantinePending") ||
                        (aStateNow==GoGpuNeuralOutputReader.READBACK_RECOVERED &&
                        ReadInt(readerAProgram, "recoveredCallbacks")>0);
                    bool bQuarantined = (ReadBool(readerBProgram, "quarantinePending") &&
                        bStateNow==GoGpuNeuralOutputReader.READBACK_QUARANTINED) ||
                        (bStateNow==GoGpuNeuralOutputReader.READBACK_RECOVERED &&
                        ReadInt(readerBProgram, "recoveredCallbacks")>0);
                    if (!aQuarantined || !bQuarantined ||
                        readerCState != GoGpuNeuralOutputReader.READBACK_WAITING)
                    {
                        FailIfTimedOut("reader C did not start after two unresolved cancellations");
                        return;
                    }

                    Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_SECOND_QUARANTINE aState=" +
                        ReadInt(readerAProgram, "readbackState") + " bState=" +
                        ReadInt(readerBProgram, "readbackState") + " cState=" + readerCState +
                        " rotations=" + ReadInt(aiProgram, "readerRotations"));
                    SetStep(4);
                    return;
                }

                if (step == 4)
                {
                    int moves = ReadInt(aiProgram, "aiMoves");
                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int visits = ReadInt(searchProgram, "visitsCompleted");
                    int rotations = ReadInt(aiProgram, "readerRotations");
                    int controllerState = ReadInt(aiProgram, "controllerState");
                    if (controllerState == GoAiController.STATE_ERROR)
                    {
                        Fail("third reader search entered error: " +
                            ReadString(aiProgram, "lastError"));
                        return;
                    }
                    if (moves > baselineAiMoves && moveCount > baselineMoveCount &&
                        visits >= GoDifficultyProfile.BEGINNER_VISITS && rotations >= 2)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_PASS aiMoves=" + moves +
                            " moveCount=" + moveCount + " visits=" + visits +
                            " rotations=" + rotations + " aState=" +
                            ReadInt(readerAProgram, "readbackState") + " bState=" +
                            ReadInt(readerBProgram, "readbackState") + " cState=" +
                            ReadInt(readerCProgram, "readbackState") + " staleA=" +
                            ReadInt(readerAProgram, "staleCallbacks") + " staleB=" +
                            ReadInt(readerBProgram, "staleCallbacks"));
                        StopWithResult(true);
                        return;
                    }
                }

                FailIfTimedOut("three-reader quarantine did not recover a fresh AI move" +
                    " phase=" + phase + " aState=" + readerAState +
                    " bState=" + readerBState + " cState=" + readerCState +
                    " aiMoves=" + ReadInt(aiProgram, "aiMoves") +
                    " visits=" + ReadInt(searchProgram, "visitsCompleted"));
            }
            catch (Exception exception)
            {
                Fail("ClientSim reader-pool inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            GoAiController aiProxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (aiProxy == null)
                return;

            if (gameProgram == null && aiProxy.game != null)
                gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.game);
            if (aiProgram == null)
                aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy);
            if (searchProgram == null && aiProxy.search != null)
                searchProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.search);
            if (readerAProgram == null && aiProxy.reader != null)
                readerAProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.reader);
            if (readerBProgram == null && aiProxy.alternateReader != null)
                readerBProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.alternateReader);
            if (readerCProgram == null && aiProxy.tertiaryReader != null)
                readerCProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.tertiaryReader);
        }

        private static void SendEvent(UdonBehaviour program, string eventName, string reason)
        {
            program.SendCustomEvent(eventName);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_EVENT name=" + eventName +
                " reason=" + reason);
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_READER_POOL_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READER_POOL_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
