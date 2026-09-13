#if UNITY_EDITOR
using System;
using TMPro;
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
    /// Exercises the generated Custom-visits numeric input and the explicit
    /// "AI move now" estimate action through real Button.onClick events.
    /// </summary>
    public static class GoClientSimCustomVisitsVerifier
    {
        private const double TimeoutSeconds = 180.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimCustomVisitsVerifier.Pending";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int stage;
        private static int stageFrames;
        private static GoUI uiProxy;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour telemetryProgram;
        private static UdonBehaviour profileProgram;
        private static UdonBehaviour whiteProfileProgram;
        private static UdonBehaviour settingsProgram;
        private static TMP_InputField blackVisitsInput;
        private static Button customButton;
        private static Button applyButton;
        private static Button beginnerButton;
        private static Button whiteStartsButton;
        private static Button startButton;
        private static Button moveNowButton;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false)) return;
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Custom Visits And Immediate AI Move")]
        public static void VerifyCustomVisitsAndImmediateMove()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded.");
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
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_CUSTOM_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            SessionState.SetBool(PendingSessionKey, false);
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            stage = 0;
            stageFrames = 0;
            uiProxy = null;
            uiProgram = null;
            gameProgram = null;
            aiProgram = null;
            telemetryProgram = null;
            settingsProgram = null;
            customButton = null;
            applyButton = null;
            beginnerButton = null;
            whiteStartsButton = null;
            startButton = null;
            moveNowButton = null;
            blackVisitsInput = null;
            whiteProfileProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_CUSTOM_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before custom/immediate gate completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying) return;
            if (!enteredPlayMode) enteredPlayMode = true;
            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize"); return;
                }
                ResolvePrograms();
                if (uiProgram == null || gameProgram == null || aiProgram == null ||
                    telemetryProgram == null || settingsProgram == null || blackVisitsInput == null || customButton == null ||
                    applyButton == null || beginnerButton == null || whiteStartsButton == null ||
                    startButton == null || moveNowButton == null)
                {
                    FailIfTimedOut("generated custom-visits/immediate controls were not found"); return;
                }
                stageFrames++;
                if (stage == 0)
                {
                    // ClientSim can enter play mode before sibling Udon Start
                    // callbacks have all run.  Wait for GoUI's first complete
                    // refresh so the test exercises the real initialized
                    // settings draft rather than racing initialization.
                    if (ReadInt(uiProgram, "displayedGameRevision") < 0)
                        return;
                    if (blackVisitsInput.contentType != TMP_InputField.ContentType.IntegerNumber ||
                        blackVisitsInput.characterValidation != TMP_InputField.CharacterValidation.Integer)
                    { Fail("custom visits input is not an integer-number field"); return; }
                    Press(customButton, "PRESET CUSTOM");
                    stage = 1; stageFrames = 0; return;
                }
                if (stage == 1)
                {
                    blackVisitsInput.text = "17";
                    if (stageFrames >= 3)
                    {
                        if (profileProgram != null &&
                            ReadInt(profileProgram, "preset") != GoDifficultyProfile.BEGINNER)
                        {
                            Fail("preset draft mutated the synchronized profile before Apply");
                            return;
                        }
                        Press(applyButton, "APPLY CUSTOM 17");
                        stage = 2; stageFrames = 0;
                    }
                    return;
                }
                if (stage == 2)
                {
                    float custom17T=Mathf.Log(17f/(float)GoDifficultyProfile.BEGINNER_VISITS)/
                        Mathf.Log((float)GoDifficultyProfile.ADVANCED_VISITS/
                            (float)GoDifficultyProfile.BEGINNER_VISITS);
                    float expectedCustom17Cpuct=Mathf.Lerp(1.85f,1.60f,custom17T);
                    int expectedCustom17TopK=Mathf.RoundToInt(Mathf.Lerp(24f,64f,custom17T));
                    // Read the backing Udon program rather than the editor
                    // proxy: ClientSim executes the serialized VM state and
                    // the proxy fields are intentionally not live mirrors.
                    if (profileProgram != null &&
                        ReadInt(profileProgram, "preset") == GoDifficultyProfile.CUSTOM &&
                        ReadInt(profileProgram, "maxVisits") == 17 &&
                         ReadInt(profileProgram, "maxNNQueries") == 17 &&
                         ReadInt(profileProgram, "maxTransitionsPerFrame") == 2 &&
                         Mathf.Abs(ReadFloat(profileProgram, "cpuct")-expectedCustom17Cpuct)<0.001f &&
                         Mathf.Abs(ReadFloat(profileProgram, "moveTemperature")-0.10f)<0.001f &&
                         ReadInt(profileProgram, "policyTopK") == expectedCustom17TopK &&
                         Mathf.Abs(ReadFloat(profileProgram, "resignThreshold")-(-1f))<0.001f &&
                         ReadBool(settingsProgram, "blackUseCustom") &&
                         ReadInt(settingsProgram, "blackCustomVisits") == 17)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_CUSTOM_PROFILE visits=17");
                        Press(FindButton(uiProxy, 51), "BLACK FOLLOW SHARED");
                        Press(beginnerButton, "RESTORE BEGINNER 8 DRAFT");
                        Press(applyButton, "APPLY BEGINNER 8");
                        stage = 3; stageFrames = 0;
                    }
                    else FailIfTimedOut("custom visits value was not applied to the selected profile");
                    return;
                }
                if (stage == 3)
                {
                    if (profileProgram != null &&
                        ReadInt(profileProgram, "preset") == GoDifficultyProfile.BEGINNER &&
                        ReadInt(profileProgram, "maxVisits") == GoDifficultyProfile.BEGINNER_VISITS)
                    {
                        uiProgram.SendCustomEvent("SetWhiteStarts");
                        Press(startButton, "START MATCH");
                        stage = 4; stageFrames = 0;
                    }
                    else FailIfTimedOut("Beginner profile was not restored to 8 visits");
                    return;
                }
                if (stage == 4)
                {
                    if (moveNowButton.interactable)
                    {
                        Press(moveNowButton, "AI MOVE NOW");
                        stage = 5; stageFrames = 0;
                    }
                    else FailIfTimedOut("AI current estimate did not become available");
                    return;
                }
                if (stage == 5)
                {
                    bool moved = ReadInt(gameProgram, "moveCount") > 0 &&
                        ReadInt(aiProgram, "aiMoves") > 0;
                    bool finalSnapshot = ReadBool(telemetryProgram, "hasFinalAnalysis") &&
                        ReadString(telemetryProgram, "finalAnalysisSnapshot").Length > 0;
                    if (moved && finalSnapshot)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_CUSTOM_PASS visits=17 restored=8" +
                            " aiMoves=" + ReadInt(aiProgram, "aiMoves") +
                            " moveCount=" + ReadInt(gameProgram, "moveCount") +
                            " finalAnalysis=True");
                        StopWithResult(true); return;
                    }
                    FailIfTimedOut("AI immediate estimate did not commit a move and final snapshot");
                }
            }
            catch (Exception exception)
            {
                Fail("custom/immediate ClientSim exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            if (uiProxy == null) uiProxy = UnityEngine.Object.FindObjectOfType<GoUI>();
            if (uiProxy == null) return;
            if (uiProgram == null) uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy);
            GoGame game = uiProxy.game;
            if (game != null && gameProgram == null)
                gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(game);
            if (profileProgram == null && uiProxy.blackDifficulty != null)
                profileProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.blackDifficulty);
            if (whiteProfileProgram == null && uiProxy.whiteDifficulty != null)
                whiteProfileProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.whiteDifficulty);
            if (settingsProgram == null && uiProxy.aiSettings != null)
                settingsProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.aiSettings);
            if (aiProgram == null && uiProxy.aiController != null)
                aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.aiController);
            if (telemetryProgram == null && uiProxy.telemetry != null)
                telemetryProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.telemetry);
            blackVisitsInput = uiProxy.blackCustomVisitsInput;
            customButton = FindButton(uiProxy, 52);
            applyButton = FindButton(uiProxy, 23);
            beginnerButton = FindButton(uiProxy, 10);
            whiteStartsButton = FindButton(uiProxy, 39);
            startButton = FindButton(uiProxy, 34);
            moveNowButton = FindButton(uiProxy, 50);
        }

        private static Button FindButton(GoUI ui, int action)
        {
            if (ui == null || ui.controlButtons == null || ui.controlButtonActions == null) return null;
            int count = Mathf.Min(ui.controlButtons.Length, ui.controlButtonActions.Length);
            for (int i = 0; i < count; i++)
                if (ui.controlButtonActions[i] == action) return ui.controlButtons[i];
            return null;
        }

        private static void Press(Button button, string label)
        {
            if (button == null || button.onClick == null ||
                button.onClick.GetPersistentEventCount() != 1)
                throw new InvalidOperationException("generated Button.onClick is incomplete: " + label);
            button.onClick.Invoke();
            Debug.Log("PURE_UDON_GO_CLIENTSIM_CUSTOM_BUTTON label=" + label +
                " path=UnityButton.onClick");
        }

        private static int ReadInt(UdonBehaviour program, string name)
        {
            object value = program == null ? null : program.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static float ReadFloat(UdonBehaviour program, string name)
        {
            object value = program == null ? null : program.GetProgramVariable(name);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static bool ReadBool(UdonBehaviour program, string name)
        {
            object value = program == null ? null : program.GetProgramVariable(name);
            return value != null && Convert.ToBoolean(value);
        }

        private static string ReadString(UdonBehaviour program, string name)
        {
            object value = program == null ? null : program.GetProgramVariable(name);
            return value == null ? "" : value.ToString();
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup < deadline) return;
            Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten) return;
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_CUSTOM_FAIL " + message);
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.Exit(1);
        }

        private static void StopWithResult(bool passed)
        {
            if (resultWritten) return;
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_CUSTOM_RESULT pass=" + passed);
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
#endif
