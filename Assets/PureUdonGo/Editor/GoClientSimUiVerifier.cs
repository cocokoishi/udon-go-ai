#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Exercises the generated world-space UI through the same serialized
    /// GoUiButton.Press events used by the production Button listeners. It
    /// verifies separate language replacement, PvP side-wall placement, and a
    /// real PUCT/GPU hint that does not commit a move.
    /// </summary>
    public static class GoClientSimUiVerifier
    {
        private const double TimeoutSeconds = 180.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimUiVerifier.Pending";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int step;
        private static int baselineMoveCount;
        private static int baselineAiMoves;
        private static int baselineRevision;
        private static int initialUiRefreshCount;
        private static int hintUiRefreshCount;
        private static int stepFrameCount;
        private static bool joinDiagnosticLogged;
        private static bool pvpStartIssued;
        private static bool pvpRestartStartIssued;
        private static bool finalChineseRequested;
        private static string lastAnalysisText="";
        private static string lastTelemetryState="";
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;
        private static GoTelemetry telemetryProxy;
        private static Button pvpButton;
        private static Button aiAiButton;
        private static Button joinBlackButton;
        private static Button joinWhiteButton;
        private static Button startButton;
        private static Button hintPermissionButton;
        private static Button languageButton;
        private static Button hintButton;
        private static Button drawButton;
        private static Button newGameButton;
        private static Button advancedAnalysisButton;
        private static Button forceResetButton;
        private static TMPro.TMP_Text appliedProfileText;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false))
                return;
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_REATTACHED step=" +
                SessionState.GetInt("PureUdonGo.ClientSimUiVerifier.Step", 0));
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim UI And Hint")]
        public static void VerifyClientSimUiAndHint()
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
            SessionState.SetInt("PureUdonGo.ClientSimUiVerifier.Step", 0);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds);
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
            baselineMoveCount = 0;
            baselineAiMoves = 0;
            baselineRevision = 0;
            initialUiRefreshCount = 0;
            hintUiRefreshCount = 0;
            stepFrameCount = 0;
            joinDiagnosticLogged = false;
            pvpStartIssued = false;
            pvpRestartStartIssued = false;
            finalChineseRequested = false;
            lastAnalysisText = "";
            lastTelemetryState = "";
            gameProgram = null;
            aiProgram = null;
            uiProgram = null;
            searchProgram = null;
            readerProgram = null;
            telemetryProxy = null;
            pvpButton = null;
            aiAiButton = null;
            joinBlackButton = null;
            joinWhiteButton = null;
            startButton = null;
            hintPermissionButton = null;
            languageButton = null;
            hintButton = null;
            drawButton = null;
            newGameButton = null;
            advancedAnalysisButton = null;
            forceResetButton = null;
            appliedProfileText = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before the UI/hint probe completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_PLAYMODE_ENTERED");
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
                    pvpButton == null || joinBlackButton == null ||
                    joinWhiteButton == null || startButton == null ||
                    hintPermissionButton == null || aiAiButton == null ||
                    languageButton == null || hintButton == null ||
                    drawButton == null || newGameButton == null ||
                    forceResetButton == null || appliedProfileText == null)
                {
                    FailIfTimedOut("generated UI Udon programs/buttons were not all found");
                    return;
                }

                bool blackIsAI = ReadBool(gameProgram, "blackIsAI");
                bool whiteIsAI = ReadBool(gameProgram, "whiteIsAI");
                bool english = ReadBool(uiProgram, "englishLanguage");
                stepFrameCount++;
                if (step == 0)
                {
                    // ClientSim resolves the generated UdonBehaviour graph
                    // before the first Udon Start/Update turn. Let that first
                    // turn apply the state-driven Button interactability
                    // before inspecting visible controls.
                    if (stepFrameCount < 3)
                        return;
                    initialUiRefreshCount = ReadInt(uiProgram, "refreshCount");
                    if (appliedProfileText.text == null ||
                        appliedProfileText.text.IndexOf("APPLIED", StringComparison.OrdinalIgnoreCase) < 0 ||
                        appliedProfileText.text.IndexOf("SHARED", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException(
                            "applied profile summary is missing: " + appliedProfileText.text);
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_INITIAL_STATE gameState=" +
                        ReadInt(gameProgram, "gameState") + " matchStarted=" +
                        ReadBool(gameProgram, "matchStarted") + " sideToMove=" +
                        ReadInt(gameProgram, "sideToMove") + " blackIsAI=" +
                        ReadBool(gameProgram, "blackIsAI") + " whiteIsAI=" +
                        ReadBool(gameProgram, "whiteIsAI") + " uiRefreshCount=" +
                        initialUiRefreshCount);
                    AssertControlState(1, false, "PASS before a seated human turn");
                    AssertControlState(2, false, "RESIGN before a seated human turn");
                    AssertControlState(44, false, "UNDO before the first move");
                    AssertControlState(45, false, "DRAW before PvP is active");
                    AssertControlState(34, true, "START while default PvAI is ready");
                    SendButton(aiAiButton, "AI VS AI");
                    SetStep(9);
                    return;
                }
                if (step == 9)
                {
                    if (stepFrameCount < 3)
                        return;
                    if (blackIsAI && whiteIsAI && PanelYawIs(0f))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_AIVAI_PASS panelYaw=0");
                        SendButton(pvpButton, "PVP");
                        SetStep(1);
                        return;
                    }
                }
                else if (step == 1)
                {
                    if (stepFrameCount < 60)
                        return;
                    int idleRefreshCount = ReadInt(uiProgram, "refreshCount");
                    if (idleRefreshCount - initialUiRefreshCount > 10)
                        throw new InvalidOperationException(
                            "idle UI rebuilt too often: refreshDelta=" +
                            (idleRefreshCount - initialUiRefreshCount));
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_REFRESH_GATE frames=" +
                        stepFrameCount + " refreshDelta=" +
                        (idleRefreshCount - initialUiRefreshCount));
                    if (!LocalPlayerReady())
                    {
                        FailIfTimedOut("ClientSim local player was not ready for seat claim");
                        return;
                    }
                    if (!blackIsAI && !whiteIsAI && PanelYawIs(90f))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_PVP_PASS panelYaw=90");
                        SendButton(joinBlackButton, "JOIN BLACK");
                        SetStep(2);
                        return;
                    }
                }
                else if (step == 2)
                {
                    if (!joinDiagnosticLogged)
                    {
                        joinDiagnosticLogged = true;
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_JOIN_STATE blackId=" +
                            ReadInt(gameProgram, "blackPlayerId") + " whiteId=" +
                            ReadInt(gameProgram, "whitePlayerId") + " matchStarted=" +
                            ReadBool(gameProgram, "matchStarted") + " action=" +
                            ReadString(gameProgram, "lastActionText"));
                    }
                    if (!blackIsAI && !whiteIsAI && ReadInt(gameProgram, "blackPlayerId") >= 0 &&
                        !ReadBool(gameProgram, "matchStarted") && !pvpStartIssued)
                    {
                        AssertControlState(31, true, "JOIN WHITE remains available for the second PvP owner");
                        AssertControlState(34, true, "START after a PvP seat claim");
                        SendButton(hintPermissionButton, "ENABLE AI HINT BEFORE PVP START");
                        SendButton(startButton, "START PVP");
                        pvpStartIssued = true;
                        return;
                    }
                    if (!blackIsAI && !whiteIsAI && ReadInt(gameProgram, "blackPlayerId") >= 0 &&
                        ReadBool(gameProgram, "matchStarted"))
                    {
                        AssertControlState(1, true, "PASS for the seated Black player");
                        AssertControlState(45, true, "DRAW for the seated PvP player");
                        AssertControlState(44, false, "UNDO before the first move");
                        AssertControlState(49, false, "AI hint permission is locked after PVP start");
                        SendButton(languageButton, "LANGUAGE TO CHINESE");
                        SetStep(3);
                        return;
                    }
                }
                else if (step == 3)
                {
                    string analysis = ReadAnalysisText();
                    if (!english && LanguageArraysAreParallel() &&
                        HasFixedAnalysisTemplate(analysis, false))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_LANGUAGE_PASS english=False arrays=parallel center=localized");
                        SendButton(languageButton, "LANGUAGE TO ENGLISH");
                        SetStep(4);
                        return;
                    }
                }
                else if (step == 4)
                {
                    string analysis = ReadAnalysisText();
                    if (english && !blackIsAI && !whiteIsAI &&
                        ReadInt(gameProgram, "blackPlayerId") >= 0 &&
                        HasFixedAnalysisTemplate(analysis, true))
                    {
                        SendButton(drawButton, "OFFER DRAW");
                        SetStep(5);
                        return;
                    }
                }
                else if (step == 5)
                {
                    if (ReadInt(gameProgram, "drawOfferSide") == GoGame.BLACK &&
                        ReadInt(gameProgram, "gameState") == GoGame.STATE_PLAYING)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_DRAW_PASS offerSide=" +
                            ReadInt(gameProgram, "drawOfferSide") + " moveCount=" +
                            ReadInt(gameProgram, "moveCount"));
                        SendButton(newGameButton, "NEW GAME");
                        SetStep(6);
                        return;
                    }
                }
                else if (step == 6)
                {
                    if (ReadInt(gameProgram, "drawOfferSide") == GoGame.EMPTY &&
                        ReadInt(gameProgram, "moveCount") == 0)
                    {
                        if (!ReadBool(gameProgram, "matchStarted"))
                        {
                            if (!pvpRestartStartIssued)
                            {
                                SendButton(hintPermissionButton, "ENABLE AI HINT BEFORE NEW PVP START");
                                SendButton(startButton, "RESTART PVP");
                                pvpRestartStartIssued = true;
                            }
                            return;
                        }
                        AssertControlState(49, false, "AI hint permission is locked after the new PVP start");
                        AssertControlState(43, true, "AI Hint on the active human turn");
                        baselineMoveCount = ReadInt(gameProgram, "moveCount");
                        baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                        baselineRevision = ReadInt(gameProgram, "revision");
                        hintUiRefreshCount = ReadInt(uiProgram, "refreshCount");
                        SendButton(hintButton, "AI HINT");
                        SetStep(7);
                        return;
                    }
                }
                else if (step == 7)
                {
                    if (ReadBool(aiProgram, "hintRequested"))
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_HINT_STARTED moveCount=" +
                            ReadInt(gameProgram, "moveCount") + " revision=" +
                            ReadInt(gameProgram, "revision"));
                        SetStep(8);
                        return;
                    }
                }
                else if (step == 8)
                {
                    bool hintRequested = ReadBool(aiProgram, "hintRequested");
                    int hintMove = ReadInt(gameProgram, "hintMove");
                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int aiMoves = ReadInt(aiProgram, "aiMoves");
                    int visits = searchProgram == null ? 0 : ReadInt(searchProgram, "visitsCompleted");
                    int stages = readerProgram == null ? 0 : ReadInt(readerProgram, "completedStages");
                    int ownershipReads = readerProgram == null ? 0 :
                        ReadInt(readerProgram, "ownershipReadbackCount");
                    string analysisText = ReadAnalysisText();
                    lastAnalysisText = analysisText;
                    UdonBehaviour telemetryProgram = telemetryProxy == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(telemetryProxy);
                    int finalTargetVisits = telemetryProgram == null ? 0 :
                        ReadInt(telemetryProgram, "finalAnalysisTargetVisits");
                    int finalCompletedVisits = telemetryProgram == null ? 0 :
                        ReadInt(telemetryProgram, "finalAnalysisCompletedVisits");
                    bool finalProgressComplete = finalTargetVisits > 0 &&
                        finalCompletedVisits == finalTargetVisits;
                    // Once the hint search has committed its final snapshot,
                    // the center card must leave the live "Thinking" state and
                    // show the session counter (target/target for the fixture),
                    // not the root-edge N-1 counter.
                    bool finalPanelSettled = analysisText.IndexOf(
                            "Thinking", StringComparison.OrdinalIgnoreCase) < 0 &&
                        analysisText.IndexOf(
                            "SEARCH  " + finalCompletedVisits + "/" + finalTargetVisits,
                            StringComparison.OrdinalIgnoreCase) >= 0;
                    bool analysisUseful = HasFixedAnalysisTemplate(analysisText, english) &&
                        analysisText.IndexOf("REV ", StringComparison.OrdinalIgnoreCase) < 0;
                    if (!hintRequested && hintMove != GoGame.NONE &&
                        moveCount == baselineMoveCount && aiMoves == baselineAiMoves &&
                        visits >= GoDifficultyProfile.BEGINNER_VISITS && ownershipReads >= 1 &&
                        analysisUseful && finalProgressComplete && finalPanelSettled)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_HINT_PASS hintMove=" +
                            Coordinate(hintMove) + " baselineMoveCount=" + baselineMoveCount +
                            " finalMoveCount=" + moveCount + " baselineAiMoves=" + baselineAiMoves +
                            " finalAiMoves=" + aiMoves + " visits=" + visits +
                            " readerCompletedStages=" + stages + " ownershipReadbacks=" + ownershipReads +
                            " uiRefreshCount=" + ReadInt(uiProgram, "refreshCount") +
                            " uiRefreshesDuringHint=" +
                            (ReadInt(uiProgram, "refreshCount") - hintUiRefreshCount) +
                            " analysisUseful=" + analysisUseful +
                            " finalProgress=" + finalCompletedVisits + "/" + finalTargetVisits +
                            " finalPanelSettled=" + finalPanelSettled +
                            " revisionBefore=" + baselineRevision +
                            " revisionAfter=" + ReadInt(gameProgram, "revision"));
                        SendButton(advancedAnalysisButton,"ADVANCED ANALYSIS");
                        // Use a distinct state for advanced-analysis
                        // verification; step 9 is the earlier AIvAI layout
                        // transition state.
                        SetStep(10);
                        return;
                    }
                }
                else if(step==10)
                {
                    string advancedText=ReadAnalysisText();
                    bool advancedUseful=HasFixedAnalysisTemplate(advancedText, true)&&
                        advancedText.IndexOf("REV ",StringComparison.OrdinalIgnoreCase)<0;
                    if(advancedUseful)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_ADVANCED_PASS defaultCollapsed=True fixedElevenLineAbi=True");
                        if(!finalChineseRequested)
                        {
                            finalChineseRequested=true;
                            SendButton(languageButton,"FINAL ANALYSIS TO CHINESE");
                            SetStep(11);
                            return;
                        }
                    }
                }
                else if(step==11)
                {
                    string chineseFinalText=ReadAnalysisText();
                    bool chineseFinal=HasFixedAnalysisTemplate(chineseFinalText,false)&&
                        chineseFinalText.IndexOf("AI analysis complete",StringComparison.OrdinalIgnoreCase)<0&&
                        chineseFinalText.IndexOf("AI hint analysis complete",StringComparison.OrdinalIgnoreCase)<0&&
                        chineseFinalText.IndexOf("AI immediate estimate",StringComparison.OrdinalIgnoreCase)<0;
                    if(chineseFinal)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_FINAL_CHINESE_PASS fixedElevenLineAbi=True finalStatusLocalized=True");
                        StopWithResult(true);return;
                    }
                }
                    string advancedDiagnostic = step == 10 ? CompactDiagnostic(ReadAnalysisText()) : "";
                    string advancedIndexes = "";
                    if(step==10)
                    {
                        string t=ReadAnalysisText();
                        advancedIndexes=t.IndexOf("CANDIDATES",StringComparison.OrdinalIgnoreCase)+","+
                            t.IndexOf("WINRATE",StringComparison.OrdinalIgnoreCase)+","+
                            t.IndexOf("SCORE LEAD W−B",StringComparison.OrdinalIgnoreCase)+","+
                            t.IndexOf("WHITE OWNERSHIP MEAN",StringComparison.OrdinalIgnoreCase)+","+
                            t.IndexOf("REV ",StringComparison.OrdinalIgnoreCase);
                    }
                    bool uiAdvancedFlag = uiProgram != null && ReadBool(uiProgram, "advancedAnalysisEnabled");
                    bool telemetryAdvancedFlag = telemetryProxy != null &&
                        ReadBool(UdonSharpEditorUtility.GetBackingUdonBehaviour(telemetryProxy), "showAdvancedAnalysis");
                    FailIfTimedOut("UI/hint probe did not advance step=" + step +
                    " mode=" + ReadInt(aiProgram, "mode") + " blackIsAI=" + blackIsAI +
                    " whiteIsAI=" + whiteIsAI + " english=" + english +
                    " panelYaw=" + ReadPanelYaw() + " moveCount=" +
                    ReadInt(gameProgram, "moveCount") + " hintMove=" +
                    ReadInt(gameProgram, "hintMove") + " visits=" +
                    (searchProgram == null ? 0 : ReadInt(searchProgram, "visitsCompleted")) +
                        (step == 8 ? " analysisText=" + CompactDiagnostic(lastAnalysisText +
                         " || " + lastTelemetryState) : "") +
                        (step == 10 ? " advanced=" + advancedDiagnostic +
                         " flags=" + uiAdvancedFlag + ":" + telemetryAdvancedFlag+
                         " indexes="+advancedIndexes : ""));
            }
            catch (Exception exception)
            {
                Fail("ClientSim UI/hint inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            // A generated world contains 16 GoUI/GoGame copies. Resolve every
            // probe object through the synchronized pool's primary table;
            // independent FindObjectOfType calls can select different tables
            // and make a valid Button event look like a no-op.
            GoBoardPool poolProxy = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoUI uiProxy = poolProxy == null ? null : poolProxy.primaryUI;
            GoGame gameProxy = uiProxy == null ? null : uiProxy.game;
            if (gameProxy == null)
                gameProxy = UnityEngine.Object.FindObjectOfType<GoGame>();
            if (gameProxy != null)
            {
                gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy);
                telemetryProxy = gameProxy.telemetry;
                GoAiController aiProxy = gameProxy.aiController;
                if (aiProxy != null)
                {
                    aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy);
                    searchProgram = aiProxy.search == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.search);
                    readerProgram = aiProxy.reader == null ? null :
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy.reader);
                }
            }
            if (uiProxy == null)
                uiProxy = UnityEngine.Object.FindObjectOfType<GoUI>();
            if (uiProxy != null)
                uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy);

            // A pooled world contains hidden table copies. Resolve controls from
            // the same primary GoUI program as the game instead of selecting the
            // last matching endpoint from inactive tables.
            if (uiProxy != null)
            {
                Button[] controls = uiProxy.controlButtons;
                int[] actions = uiProxy.controlButtonActions;
                if (controls == null || actions == null || controls.Length != actions.Length)
                {
                    controls = uiProgram.GetProgramVariable("controlButtons") as Button[];
                    actions = uiProgram.GetProgramVariable("controlButtonActions") as int[];
                }
                if (controls != null && actions != null)
                {
                    int count = controls.Length < actions.Length ? controls.Length : actions.Length;
                    for (int i = 0; i < count; i++)
                    {
                        if (actions[i] == 3) pvpButton = controls[i];
                        else if (actions[i] == 6) aiAiButton = controls[i];
                        else if (actions[i] == 30) joinBlackButton = controls[i];
                        else if (actions[i] == 31) joinWhiteButton = controls[i];
                        else if (actions[i] == 34) startButton = controls[i];
                        else if (actions[i] == 49) hintPermissionButton = controls[i];
                        else if (actions[i] == 22) languageButton = controls[i];
                        else if (actions[i] == 43) hintButton = controls[i];
                        else if (actions[i] == 45) drawButton = controls[i];
                        else if (actions[i] == 0) newGameButton = controls[i];
                        else if (actions[i] == 48) advancedAnalysisButton = controls[i];
                        else if (actions[i] == 40) forceResetButton = controls[i];
                    }
                }
                appliedProfileText = uiProxy.panelAppliedProfileText;
            }
        }

        private static void SendButton(Button button, string label)
        {
            if (button == null)
                throw new InvalidOperationException("Missing generated Unity Button: " + label);
            if (button.onClick == null || button.onClick.GetPersistentEventCount() != 1)
                throw new InvalidOperationException("Generated Unity Button has no single persistent Udon listener: " + label);
            button.onClick.Invoke();
            Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_EVENT button=" + label + " path=UnityButton.onClick");
        }

        private static void AssertControlState(int action, bool expected, string label)
        {
            if (uiProgram == null)
                throw new InvalidOperationException("UI program is missing while checking " + label);
            Button[] buttons = uiProgram.GetProgramVariable("controlButtons") as Button[];
            int[] actions = uiProgram.GetProgramVariable("controlButtonActions") as int[];
            if (buttons == null || actions == null || buttons.Length != actions.Length)
                throw new InvalidOperationException("state-driven control arrays are incomplete while checking " + label);
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] != action)
                    continue;
                if (buttons[i] == null || buttons[i].interactable != expected)
                    throw new InvalidOperationException("interactable mismatch for " + label +
                        " expected=" + expected + " actual=" +
                        (buttons[i] != null && buttons[i].interactable));
                Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_CONTROL action=" + action +
                    " interactable=" + expected + " label=" + label);
                return;
            }
            throw new InvalidOperationException("control action binding is missing: " + action);
        }

        private static bool LanguageArraysAreParallel()
        {
            Array chinese = uiProgram.GetProgramVariable("chineseTexts") as Array;
            Array english = uiProgram.GetProgramVariable("englishTexts") as Array;
            Array texts = uiProgram.GetProgramVariable("localizedTexts") as Array;
            return chinese != null && english != null && texts != null &&
                chinese.Length > 0 && chinese.Length == english.Length &&
                chinese.Length == texts.Length;
        }

        private static bool HasFixedAnalysisTemplate(string value, bool english)
        {
            if (value == null)
                return false;
            string[] lines = value.Replace("\r", "").Split('\n');
            if (lines.Length != 11)
                return false;
            string[] labels = english
                ? new string[] { "AI STATE  ", "SEARCH  ", "BEST MOVE  ", "CANDIDATES  ",
                    "WINRATE  B ", "SCORE LEAD W−B ", "WHITE OWNERSHIP MEAN ", "LEVEL  ",
                    "MOVE  ", "STATIC CN SCORE W−B ", "COMPUTE  " }
                : new string[] { "AI 状态  ", "搜索  ", "推荐着法  ", "候选着法 ",
                    "胜率  黑 ", "预计分差 白−黑 ", "白方领地均值 ", "档位  ",
                    "手数 ", "折合子差 白−黑 ", "计算端 " };
            for (int i = 0; i < labels.Length; i++)
                if (!lines[i].StartsWith(labels[i], StringComparison.Ordinal))
                    return false;
            if (english)
            {
                if (lines[9].IndexOf(" zi", StringComparison.Ordinal) < 0)
                    return false;
            }
            else if (lines[9].IndexOf(" 子", StringComparison.Ordinal) < 0)
                return false;
            return true;
        }

        private static string ReadAnalysisText()
        {
            GoTelemetry telemetry = telemetryProxy;
            if (telemetry == null)
                telemetry = UnityEngine.Object.FindObjectOfType<GoTelemetry>();
            if (telemetry == null || telemetry.panelText == null)
            {
                lastTelemetryState = "telemetry=missing";
                return "";
            }
            UdonBehaviour program = UdonSharpEditorUtility.GetBackingUdonBehaviour(telemetry);
            lastTelemetryState = program == null ? "telemetryProgram=missing" :
                "telemetryRevision=" + ReadInt(program, "revision") +
                " searchPhase=" + ReadInt(program, "searchPhase") +
                " targetVisits=" + ReadInt(program, "targetVisits") +
                " completedVisits=" + ReadInt(program, "completedVisits") +
                " candidateVisits=" + ReadInt(program, "candidateVisits0") + "," +
                    ReadInt(program, "candidateVisits1") + "," + ReadInt(program, "candidateVisits2") +
                " outputRevision=" + ReadInt(program, "outputRevision") +
                " status=" + CompactDiagnostic(ReadString(program, "statusText"));
            return telemetry.panelText.text ?? "";
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

        private static bool LocalPlayerReady()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && local.playerId > 0;
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

        private static string Coordinate(int location)
        {
            if (location == GoGame.PASS)
                return "PASS";
            if (location < 0 || location >= GoGame.AREA)
                return "NONE";
            const string columns = "ABCDEFGHJKLMNOPQRST";
            return columns[location % GoGame.SIZE].ToString() +
                (GoGame.SIZE - location / GoGame.SIZE).ToString();
        }

        private static void SetStep(int value)
        {
            step = value;
            stepFrameCount = 0;
            SessionState.SetInt("PureUdonGo.ClientSimUiVerifier.Step", value);
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup < deadline)
                return;
            Fail(message);
        }

        private static string CompactDiagnostic(string value)
        {
            if (value == null)
                return "";
            string compact = value.Replace("\r", " ").Replace("\n", " / ");
            if (compact.Length > 700)
                compact = compact.Substring(0, 700);
            return compact;
        }

        private static void Fail(string message)
        {
            if (resultWritten)
                return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_UI_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_UI_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
