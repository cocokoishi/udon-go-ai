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
    /// Verifies that a real ClientSim reset invalidates an in-flight Udon GPU
    /// result. The editor harness only sends the public UI/game events and
    /// reads serialized Udon variables; cancellation and stale-callback
    /// handling run inside the compiled Udon programs.
    /// </summary>
    public static class GoClientSimStaleResultVerifier
    {
        private const double TimeoutSeconds = 120.0;
        private const double CancelSettleSeconds = 30.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimStaleResultVerifier.Pending";
        private const string StepSessionKey =
            "PureUdonGo.ClientSimStaleResultVerifier.Step";

        private static double deadline;
        private static double cancelDeadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int step;
        private static int baselineRevision;
        private static int baselineAiMoves;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_REATTACHED step=" + step);
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Stale Result Protection")]
        public static void VerifyClientSimStaleResultProtection()
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

            SessionState.SetBool(PendingSessionKey, true);
            SessionState.SetInt(StepSessionKey, 0);
            step = 0;
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" +
                TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            cancelDeadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            step = 0;
            baselineRevision = 0;
            baselineAiMoves = 0;
            gameProgram = null;
            aiProgram = null;
            uiProgram = null;
            searchProgram = null;
            readerProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before stale-result verification completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }

                ResolvePrograms();
                if (gameProgram == null || aiProgram == null || uiProgram == null ||
                    searchProgram == null || readerProgram == null)
                {
                    FailIfTimedOut("Generated Go Udon programs were not all found");
                    return;
                }

                if (step == 0)
                {
                    baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                    SendEvent(gameProgram, "SetWhiteStarts", "prepare white AI turn");
                    SendEvent(gameProgram, "RequestStartMatch", "start PvAI match");
                    SetStep(1);
                    return;
                }

                int readerState = ReadInt(readerProgram, "readbackState");
                int readerStage = ReadInt(readerProgram, "readbackStage");
                int searchPhase = ReadInt(searchProgram, "phase");
                if (step == 1)
                {
                    if (readerState == GoGpuNeuralOutputReader.READBACK_WAITING &&
                        readerStage >= 0 &&
                        searchPhase == GoMctsSearch.PHASE_WAITING_NEURAL)
                    {
                        baselineRevision = ReadInt(gameProgram, "revision");
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_PENDING revision=" +
                            baselineRevision + " readerState=" + readerState +
                            " readerStage=" + readerStage + " searchPhase=" +
                            searchPhase);
                        // Cancel the controller in the same Udon event window
                        // as the authoritative reset.  The visible UI reset is
                        // covered by the UI verifier; this probe must not let
                        // an editor update race complete an AI move before the
                        // reset endpoint is processed.
                        baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                        SendEvent(aiProgram, "InvalidateSearch", "cancel in-flight GPU result");
                        SendEvent(gameProgram, "RequestForceReset", "authoritative reset after cancellation");
                        cancelDeadline = EditorApplication.timeSinceStartup + CancelSettleSeconds;
                        SetStep(2);
                        return;
                    }
                }
                else if (step == 2)
                {
                    bool matchStarted = ReadBool(gameProgram, "matchStarted");
                    int revision = ReadInt(gameProgram, "revision");
                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int aiMoves = ReadInt(aiProgram, "aiMoves");
                    int staleCallbacks = ReadInt(readerProgram, "staleCallbacks");
                    int completedVisits = ReadInt(searchProgram, "visitsCompleted");
                    int neuralOutputRevision = ReadInt(aiProgram, "lastNeuralOutputRevision");
                    bool cancelledWithoutCommit = searchPhase == GoMctsSearch.PHASE_CANCELLED &&
                        neuralOutputRevision == 0;
                    if (!matchStarted && moveCount == 0 &&
                        aiMoves == baselineAiMoves && revision > baselineRevision &&
                        readerState != GoGpuNeuralOutputReader.READBACK_WAITING &&
                        (staleCallbacks > 0 || cancelledWithoutCommit))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_PASS revisionBefore=" +
                            baselineRevision + " revisionAfter=" + revision +
                            " moveCount=" + moveCount + " aiMoves=" + aiMoves +
                            " staleCallbacks=" + staleCallbacks +
                            " cancelledWithoutCommit=" + cancelledWithoutCommit +
                            " completedVisits=" + completedVisits +
                            " neuralOutputRevision=" + neuralOutputRevision +
                            " searchPhase=" + searchPhase + " readerState=" +
                            readerState);
                        StopWithResult(true);
                        return;
                    }
                    if (EditorApplication.timeSinceStartup >= cancelDeadline)
                    {
                        Fail("stale result was not observed after reset revision=" +
                            revision + " moveCount=" + moveCount + " aiMoves=" +
                            aiMoves + " staleCallbacks=" + staleCallbacks +
                            " cancelledWithoutCommit=" + cancelledWithoutCommit +
                            " completedVisits=" + completedVisits +
                            " neuralOutputRevision=" + neuralOutputRevision +
                            " searchPhase=" + searchPhase + " readerState=" +
                            readerState);
                        return;
                    }
                }

                FailIfTimedOut("stale-result regression did not advance step=" + step +
                    " readerState=" + readerState + " readerStage=" + readerStage +
                    " searchPhase=" + searchPhase);
            }
            catch (Exception exception)
            {
                Fail("ClientSim stale-result inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            if (gameProgram == null)
            {
                GoGame proxy = UnityEngine.Object.FindObjectOfType<GoGame>();
                if (proxy != null)
                    gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            }
            if (aiProgram == null)
            {
                GoAiController proxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
                if (proxy != null)
                {
                    aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                    searchProgram = proxy.search == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy.search);
                    readerProgram = proxy.reader == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy.reader);
                }
            }
            if (uiProgram == null)
            {
                GoUI proxy = UnityEngine.Object.FindObjectOfType<GoUI>();
                if (proxy != null)
                    uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            }
        }

        private static void SendEvent(UdonBehaviour program, string eventName, string reason)
        {
            if (program == null)
                throw new InvalidOperationException("Cannot send Udon event: " + eventName);
            program.SendCustomEvent(eventName);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_EVENT name=" + eventName +
                " reason=" + reason);
        }

        private static void SetStep(int value)
        {
            step = value;
            SessionState.SetInt(StepSessionKey, value);
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_STALE_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_STALE_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
