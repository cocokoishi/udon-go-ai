#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Exercises the user-facing mode, starter and difficulty events through
    /// the real serialized Udon programs in ClientSim. The editor harness only
    /// dispatches zero-argument Udon events and reads Udon program variables;
    /// the state changes and the final AI move are executed by UdonSharp output.
    /// </summary>
    public static class GoClientSimModesVerifier
    {
        private const double TimeoutSeconds = 240.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimModesVerifier.Pending";
        private const string StepSessionKey =
            "PureUdonGo.ClientSimModesVerifier.Step";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool configLogged;
        private static int step;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour blackProfileProgram;
        private static UdonBehaviour whiteProfileProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;
        private static GoUI uiProxy;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_REATTACHED step=" + step);
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Modes And Difficulty")]
        public static void VerifyClientSimModesAndDifficulty()
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
            step = 0;
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" +
                TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            configLogged = false;
            step = 0;
            gameProgram = null;
            aiProgram = null;
            uiProgram = null;
            blackProfileProgram = null;
            whiteProfileProgram = null;
            searchProgram = null;
            readerProgram = null;
            uiProxy = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before the mode regression completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_PLAYMODE_ENTERED");
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
                    blackProfileProgram == null || whiteProfileProgram == null)
                {
                    FailIfTimedOut("Generated Go Udon programs were not all found");
                    return;
                }

                if (!configLogged)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_CONFIG gameEvents=" +
                        DescribeEvents(gameProgram) + " uiEvents=" +
                        DescribeEvents(uiProgram) + " aiEvents=" +
                        DescribeEvents(aiProgram));
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_INITIAL blackIsAI=" +
                        ReadBool(gameProgram, "blackIsAI") + " whiteIsAI=" +
                        ReadBool(gameProgram, "whiteIsAI") + " starter=" +
                        ReadInt(gameProgram, "starter") + " blackVisits=" +
                        ReadInt(blackProfileProgram, "maxVisits") +
                        " whiteVisits=" + ReadInt(whiteProfileProgram, "maxVisits") +
                        " panelYaw=" + ReadPanelYaw());
                    Press(FindButton(3), "PvAI to PvP");
                    SetStep(1);
                    configLogged = true;
                    return;
                }

                bool blackIsAI = ReadBool(gameProgram, "blackIsAI");
                bool whiteIsAI = ReadBool(gameProgram, "whiteIsAI");

                if (step == 1)
                {
                    if (!blackIsAI && !whiteIsAI && PanelYawIs(90f))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_STATE mode=PvP panelYaw=90");
                        Press(FindButton(5), "PvP to AIvP");
                        SetStep(2);
                        return;
                    }
                }
                else if (step == 2)
                {
                    if (blackIsAI && !whiteIsAI && PanelYawIs(0f))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_STATE mode=AIvP panelYaw=0");
                        Press(FindButton(6), "AIvP to AIvAI");
                        SetStep(3);
                        return;
                    }
                }
                else if (step == 3)
                {
                    if (blackIsAI && whiteIsAI && PanelYawIs(0f))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_STATE mode=AIvAI panelYaw=0");
                        Press(FindButton(11), "shared Advanced");
                        Press(FindButton(23), "apply shared Advanced");
                        SetStep(4);
                        return;
                    }
                }
                else if (step == 4)
                {
                    if (ReadInt(blackProfileProgram, "preset") == GoDifficultyProfile.ADVANCED &&
                        ReadInt(blackProfileProgram, "maxVisits") == GoDifficultyProfile.ADVANCED_VISITS &&
                        ReadInt(blackProfileProgram, "maxNNQueries") == GoDifficultyProfile.ADVANCED_VISITS &&
                        ReadInt(whiteProfileProgram, "preset") == GoDifficultyProfile.ADVANCED)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_PROFILE shared=ADVANCED visits=" + GoDifficultyProfile.ADVANCED_VISITS);
                        Press(FindButton(12), "shared Master");
                        Press(FindButton(23), "apply shared Master");
                        SetStep(5);
                        return;
                    }
                }
                else if (step == 5)
                {
                    if (ReadInt(blackProfileProgram, "preset") == GoDifficultyProfile.MASTER &&
                        ReadInt(whiteProfileProgram, "preset") == GoDifficultyProfile.MASTER)
                    {
                        Press(FindButton(52), "black custom");
                        Press(FindButton(54), "white custom");
                        if (uiProxy.blackCustomVisitsInput != null) uiProxy.blackCustomVisitsInput.text="64";
                        if (uiProxy.whiteCustomVisitsInput != null) uiProxy.whiteCustomVisitsInput.text="96";
                        Press(FindButton(23), "apply independent custom visits");
                        SetStep(6);
                        return;
                    }
                }
                else if (step == 6)
                {
                    if (ReadInt(blackProfileProgram, "preset") == GoDifficultyProfile.CUSTOM &&
                        ReadInt(blackProfileProgram, "maxVisits") == 64 &&
                        ReadInt(whiteProfileProgram, "preset") == GoDifficultyProfile.CUSTOM &&
                        ReadInt(whiteProfileProgram, "maxVisits") == 96 &&
                        ReadInt(blackProfileProgram, "maxTransitionsPerFrame") == 5 &&
                        ReadInt(whiteProfileProgram, "maxTransitionsPerFrame") == 7)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_PROFILE custom=64/96 base=MASTER");
                        Press(FindButton(10), "shared Beginner");
                        Press(FindButton(51), "black follow shared");
                        Press(FindButton(53), "white follow shared");
                        Press(FindButton(23), "restore shared Beginner");
                        SetStep(7);
                        return;
                    }
                }
                else if (step == 7)
                {
                    if (ReadInt(blackProfileProgram, "preset") == GoDifficultyProfile.BEGINNER &&
                        ReadInt(whiteProfileProgram, "preset") == GoDifficultyProfile.BEGINNER)
                    {
                        Press(FindButton(13), "shared Ultrahard");
                        Press(FindButton(23), "apply shared Ultrahard");
                        SetStep(8);
                        return;
                    }
                }
                else if (step == 8)
                {
                    if (ReadInt(blackProfileProgram, "preset") == GoDifficultyProfile.ULTRAHARD &&
                        ReadInt(blackProfileProgram, "maxVisits") == GoDifficultyProfile.ULTRAHARD_VISITS &&
                        ReadInt(whiteProfileProgram, "preset") == GoDifficultyProfile.ULTRAHARD)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_PROFILE shared=ULTRAHARD visits=" + GoDifficultyProfile.ULTRAHARD_VISITS);
                        Press(FindButton(10), "restore shared Beginner");
                        Press(FindButton(23), "apply shared Beginner");
                        SetStep(9);
                        return;
                    }
                }
                else if (step == 9)
                {
                    if (ReadInt(blackProfileProgram, "preset") == GoDifficultyProfile.BEGINNER &&
                        ReadInt(blackProfileProgram, "maxVisits") == GoDifficultyProfile.BEGINNER_VISITS &&
                        ReadInt(blackProfileProgram, "maxNNQueries") == GoDifficultyProfile.BEGINNER_VISITS)
                    {
                        if (ReadInt(whiteProfileProgram, "preset") != GoDifficultyProfile.BEGINNER)
                        {
                            Press(FindButton(10), "restore shared Beginner for white");
                            Press(FindButton(23), "apply shared Beginner for white");
                        }
                        SetStep(11);
                        return;
                    }
                }
                else if (step == 11)
                {
                    if (ReadInt(whiteProfileProgram, "preset") == GoDifficultyProfile.BEGINNER &&
                        ReadInt(whiteProfileProgram, "maxVisits") == GoDifficultyProfile.BEGINNER_VISITS &&
                        ReadInt(whiteProfileProgram, "maxNNQueries") == GoDifficultyProfile.BEGINNER_VISITS)
                    {
                        SendEvent(uiProgram, "ForceReset", "reset AIvAI match");
                        SetStep(12);
                        return;
                    }
                }
                else if (step == 12)
                {
                    if (!ReadBool(gameProgram, "matchStarted") &&
                        ReadInt(gameProgram, "moveCount") == 0)
                    {
                        SendEvent(gameProgram, "SetWhiteStarts", "select white starter");
                        SetStep(13);
                        return;
                    }
                }
                else if (step == 13)
                {
                    if (ReadInt(gameProgram, "starter") == GoGame.WHITE &&
                        ReadInt(gameProgram, "sideToMove") == GoGame.WHITE)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_STARTER white=True");
                        SendEvent(gameProgram, "SetBlackStarts", "select black starter");
                        SetStep(14);
                        return;
                    }
                }
                else if (step == 14)
                {
                    if (ReadInt(gameProgram, "starter") == GoGame.BLACK &&
                        ReadInt(gameProgram, "sideToMove") == GoGame.BLACK)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_STARTER black=True");
                        SendEvent(gameProgram, "RequestStartMatch", "start AIvAI match");
                        SetStep(15);
                        return;
                    }
                }
                else if (step == 15)
                {
                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int aiMoves = ReadInt(aiProgram, "aiMoves");
                    int visits = searchProgram == null ? 0 :
                        ReadInt(searchProgram, "visitsCompleted");
                    int completedStages = readerProgram == null ? 0 :
                        ReadInt(readerProgram, "completedStages");
                    int ownershipReadbacks = readerProgram == null ? 0 :
                        ReadInt(readerProgram, "ownershipReadbackCount");
                    float scoreUtilityMilliseconds = searchProgram == null ? 0f :
                        ReadFloat(searchProgram, "lastScoreUtilityMilliseconds");
                    int searchFrames = ReadInt(aiProgram, "lastSearchFrames");
                    int ownershipFrames = ReadInt(aiProgram, "ownershipTelemetryFrames");
                    float featureMilliseconds = ReadFloat(aiProgram, "lastFeatureMilliseconds");
                    float gpuUploadMilliseconds = ReadFloat(aiProgram, "lastGpuUploadMilliseconds");
                    int visit0Frame = ReadInt(aiProgram, "visit0CompletionFrame");
                    int visit1Frame = ReadInt(aiProgram, "visit1CompletionFrame");
                    int visit2Frame = ReadInt(aiProgram, "visit2CompletionFrame");
                    int visit3Frame = ReadInt(aiProgram, "visit3CompletionFrame");
                    int sameFrameTransitions = ReadInt(aiProgram, "sameFramePhaseTransitions");
                    int avoidableYields = ReadInt(aiProgram, "avoidableYieldCount");
                    int deadlineYields = ReadInt(aiProgram, "deadlineYieldCount");
                    float grantedWorkMs = ReadFloat(aiProgram, "grantedFrameWorkMs");
                    float usedWorkMs = ReadFloat(aiProgram, "usedFrameWorkMs");
                    if (ReadBool(gameProgram, "matchStarted") && moveCount >= 2 &&
                        aiMoves >= 2 && visits >= GoDifficultyProfile.BEGINNER_VISITS && completedStages == 1 &&
                        ownershipReadbacks >= 1)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_PASS modes=PvP,PvAI,AIvP,AIvAI" +
                            " starter=BLACK blackVisits=" + GoDifficultyProfile.BEGINNER_VISITS +
                            " whiteVisits=" + GoDifficultyProfile.BEGINNER_VISITS +
                            " minAiMoves=2 moveCount=" + moveCount + " aiMoves=" + aiMoves +
                            " searchVisits=" + visits + " readerCompletedStages=" +
                            completedStages + " ownershipReadbacks=" + ownershipReadbacks +
                            " searchFrames=" + searchFrames + " scoreMs=" + scoreUtilityMilliseconds +
                            " ownershipFrames=" + ownershipFrames + " featureMs=" + featureMilliseconds +
                            " gpuUploadMs=" + gpuUploadMilliseconds +
                            " visitFrames=" + visit0Frame + "," + visit1Frame + "," +
                            visit2Frame + "," + visit3Frame + " sameFrameTransitions=" +
                            sameFrameTransitions + " avoidableYields=" + avoidableYields +
                            " deadlineYields=" + deadlineYields + " grantedMs=" + grantedWorkMs +
                            " usedMs=" + usedWorkMs);
                        StopWithResult(true);
                        return;
                    }
                }

                FailIfTimedOut("mode/difficulty regression did not advance step=" +
                    step + " blackIsAI=" + blackIsAI + " whiteIsAI=" + whiteIsAI +
                    " panelYaw=" + ReadPanelYaw() + " moveCount=" +
                    ReadInt(gameProgram, "moveCount") + " aiMoves=" +
                    ReadInt(aiProgram, "aiMoves"));
            }
            catch (Exception exception)
            {
                Fail("ClientSim mode inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            // A production scene contains 16 pre-generated tables. Resolve
            // every program through BoardPool.primaryUI so the event source,
            // game state and AI profiles are guaranteed to be the same table.
            GoBoardPool poolProxy = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoUI primaryUI = poolProxy == null ? null : poolProxy.primaryUI;
            uiProxy = primaryUI;
            GoGame primaryGame = primaryUI == null ? null : primaryUI.game;
            if (gameProgram == null)
            {
                GoGame gameProxy = primaryGame;
                if (gameProxy == null)
                    gameProxy = UnityEngine.Object.FindObjectOfType<GoGame>();
                if (gameProxy != null)
                {
                    gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy);
                    if (gameProxy.blackDifficulty != null)
                        blackProfileProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(
                            gameProxy.blackDifficulty);
                    if (gameProxy.whiteDifficulty != null)
                        whiteProfileProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(
                            gameProxy.whiteDifficulty);
                }
            }
            if (aiProgram == null)
            {
                GoAiController aiProxy = primaryGame == null ? null : primaryGame.aiController;
                if (aiProxy == null)
                    aiProxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
                if (aiProxy != null)
                {
                    aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy);
                    searchProgram = aiProxy.search == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.search);
                    readerProgram = aiProxy.reader == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.reader);
                }
            }
            if (uiProgram == null)
            {
                GoUI uiProxy = primaryUI;
                if (uiProxy == null)
                    uiProxy = UnityEngine.Object.FindObjectOfType<GoUI>();
                if (uiProxy != null)
                    uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy);
            }
            if (blackProfileProgram == null || whiteProfileProgram == null)
            {
                GoDifficultyProfile[] profiles =
                    UnityEngine.Object.FindObjectsOfType<GoDifficultyProfile>();
                for (int i = 0; i < profiles.Length; i++)
                {
                    if (profiles[i] == null)
                        continue;
                    UdonBehaviour program = UdonSharpEditorUtility.GetBackingUdonBehaviour(profiles[i]);
                    if (profiles[i].gameObject.name.IndexOf("Black AI Profile",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        blackProfileProgram = program;
                    else if (profiles[i].gameObject.name.IndexOf("PureUdonGo.Gameplay",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        whiteProfileProgram = program;
                }
            }
        }

        private static Button FindButton(int action)
        {
            if(uiProxy==null||uiProxy.controlButtons==null||uiProxy.controlButtonActions==null)return null;
            int count=Mathf.Min(uiProxy.controlButtons.Length,uiProxy.controlButtonActions.Length);
            for(int i=0;i<count;i++)
                if(uiProxy.controlButtonActions[i]==action)return uiProxy.controlButtons[i];
            return null;
        }

        private static void Press(Button button,string reason)
        {
            if(button==null||button.onClick==null||button.onClick.GetPersistentEventCount()!=1)
                throw new InvalidOperationException("generated mode button is incomplete: "+reason);
            button.onClick.Invoke();
            Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_BUTTON "+reason);
        }

        private static void SendEvent(UdonBehaviour program, string eventName, string reason)
        {
            if (program == null)
                throw new InvalidOperationException("Cannot send Udon event without a program: " + eventName);
            program.SendCustomEvent(eventName);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_EVENT name=" + eventName +
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

        private static float ReadFloat(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static bool ReadBool(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static float ReadPanelYaw()
        {
            if (uiProgram == null)
                return float.NaN;
            Transform mount = uiProgram.GetProgramVariable("controlPanelMount") as Transform;
            return mount == null ? float.NaN : Mathf.DeltaAngle(0f, mount.localEulerAngles.y);
        }

        private static bool PanelYawIs(float expected)
        {
            float actual = ReadPanelYaw();
            return !float.IsNaN(actual) && Mathf.Abs(Mathf.DeltaAngle(expected, actual)) < 0.5f;
        }

        private static string DescribeEvents(UdonBehaviour program)
        {
            if (program == null)
                return "null";
            string names = "";
            var events = program.GetPrograms();
            for (int i = 0; i < events.Length; i++)
            {
                if (i > 0)
                    names += ",";
                names += events[i];
            }
            return "count=" + events.Length + ",names=" + names;
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_MODES_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_MODES_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
