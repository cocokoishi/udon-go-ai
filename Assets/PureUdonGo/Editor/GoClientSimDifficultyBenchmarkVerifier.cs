#if UNITY_EDITOR
using System;
using UdonSharp;
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
    /// Runs every public Go difficulty preset through the real serialized Udon
    /// programs in ClientSim. The editor harness only sends public Udon/UI
    /// events and reads telemetry; search, feature encoding, shader dispatch,
    /// readback and move commit execute inside Udon.
    /// </summary>
    public static class GoClientSimDifficultyBenchmarkVerifier
    {
        private const double TimeoutSeconds = 1200.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimDifficultyBenchmark.Pending";
        private const string StepSessionKey =
            "PureUdonGo.ClientSimDifficultyBenchmark.Step";
        private const string TargetSessionKey =
            "PureUdonGo.ClientSimDifficultyBenchmark.Target";

        private static readonly int[] Targets =
            { GoDifficultyProfile.BEGINNER_VISITS, GoDifficultyProfile.ADVANCED_VISITS,
              GoDifficultyProfile.MASTER_VISITS, GoDifficultyProfile.ULTRAHARD_VISITS };
        private static readonly string[] PresetNames =
            { "Beginner", "Advanced", "Master", "Ultrahard" };
        private static readonly string[] PresetEvents =
            { "PresetBeginner", "PresetAdvanced", "PresetMaster", "PresetUltraHard" };

        private static double deadline;
        private static double sampleStartSeconds;
        private static int sampleStartFrame;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int step;
        private static int targetIndex;
        private static int baselineRevision;
        private static int baselineMoveCount;
        private static int baselinePositionHistoryCount;
        private static int baselineAiMoves;
        private static int baselineOwnershipReadbacks;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour whiteProfileProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;
        private static GoBoardPool poolProxy;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false))
                return;

            step = SessionState.GetInt(StepSessionKey, 0);
            targetIndex = SessionState.GetInt(TargetSessionKey, 0);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_REATTACHED step=" + step +
                " targetIndex=" + targetIndex);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Difficulty Search Ladder")]
        public static void VerifyClientSimDifficultySearchLadder()
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
            SessionState.SetInt(TargetSessionKey, 0);
            SessionState.SetBool(PendingSessionKey, true);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds +
                " presets=" + GoDifficultyProfile.BEGINNER_VISITS + "," +
                GoDifficultyProfile.ADVANCED_VISITS + "," +
                GoDifficultyProfile.MASTER_VISITS + "," +
                GoDifficultyProfile.ULTRAHARD_VISITS);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            sampleStartSeconds = 0.0;
            sampleStartFrame = 0;
            enteredPlayMode = false;
            resultWritten = false;
            step = 0;
            targetIndex = 0;
            baselineRevision = 0;
            baselineMoveCount = 0;
            baselinePositionHistoryCount = 0;
            baselineAiMoves = 0;
            baselineOwnershipReadbacks = 0;
            gameProgram = null;
            aiProgram = null;
            uiProgram = null;
            whiteProfileProgram = null;
            searchProgram = null;
            readerProgram = null;
            poolProxy = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before the difficulty ladder completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_PLAYMODE_ENTERED");
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
                    uiProgram == null || whiteProfileProgram == null ||
                    searchProgram == null || readerProgram == null)
                {
                    FailIfTimedOut("Generated Go Udon programs were not all found");
                    return;
                }

                bool blackIsAI = ReadBool(gameProgram, "blackIsAI");
                bool whiteIsAI = ReadBool(gameProgram, "whiteIsAI");

                if (step == 0)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_CONFIG blackIsAI=" +
                        blackIsAI + " whiteIsAI=" + whiteIsAI +
                        " target=" + Targets[targetIndex]);
                    if (blackIsAI)
                    {
                        SendEvent(gameProgram, "ToggleBlackController",
                            "ensure PvAI black human");
                        SetStep(90);
                        return;
                    }
                    if (!whiteIsAI)
                    {
                        SendEvent(gameProgram, "ToggleWhiteController",
                            "ensure PvAI white AI");
                        SetStep(91);
                        return;
                    }
                    SetStep(1);
                    return;
                }

                if (step == 90)
                {
                    if (!ReadBool(gameProgram, "blackIsAI"))
                        SetStep(0);
                    return;
                }

                if (step == 91)
                {
                    if (ReadBool(gameProgram, "whiteIsAI"))
                        SetStep(0);
                    return;
                }

                if (step == 1)
                {
                    SetStep(2);
                    return;
                }

                if (step == 2)
                {
                    SendEvent(uiProgram, PresetEvents[targetIndex],
                        "draft shared " + PresetNames[targetIndex] + " preset");
                    SendEvent(uiProgram, "ApplyAISettings",
                        "commit shared " + PresetNames[targetIndex] + " draft");
                    SetStep(3);
                    return;
                }

                if (step == 3)
                {
                    int target = Targets[targetIndex];
                    if (ReadInt(whiteProfileProgram, "maxVisits") != target ||
                        ReadInt(whiteProfileProgram, "maxNNQueries") != target)
                    {
                        FailIfTimedOut("profile did not apply target visits=" + target);
                        return;
                    }
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_PROFILE target=" + target +
                        " preset=" + PresetNames[targetIndex] +
                        " maxVisits=" + ReadInt(whiteProfileProgram, "maxVisits") +
                        " maxNNQueries=" + ReadInt(whiteProfileProgram, "maxNNQueries") +
                        " maxTransitionsPerFrame=" + ReadInt(whiteProfileProgram, "maxTransitionsPerFrame") +
                        " cpuct=" + ReadFloat(whiteProfileProgram, "cpuct") +
                        " temperature=" + ReadFloat(whiteProfileProgram, "moveTemperature"));
                    SendEvent(uiProgram, "ForceReset", "reset empty board before sample");
                    SetStep(4);
                    return;
                }

                if (step == 4)
                {
                    if (ReadBool(gameProgram, "matchStarted") ||
                        ReadInt(gameProgram, "moveCount") != 0)
                        return;
                    SendEvent(gameProgram, "SetWhiteStarts", "start measured white AI turn");
                    SetStep(5);
                    return;
                }

                if (step == 5)
                {
                    if (ReadInt(gameProgram, "starter") != GoGame.WHITE ||
                        ReadInt(gameProgram, "sideToMove") != GoGame.WHITE)
                        return;
                    baselineRevision = ReadInt(gameProgram, "revision");
                    baselineMoveCount = ReadInt(gameProgram, "moveCount");
                    baselinePositionHistoryCount = ReadInt(gameProgram, "positionHistoryCount");
                    baselineAiMoves = ReadInt(aiProgram, "aiMoves");
                    baselineOwnershipReadbacks = ReadInt(readerProgram, "ownershipReadbackCount");
                    sampleStartSeconds = EditorApplication.timeSinceStartup;
                    sampleStartFrame = Time.frameCount;
                    SendEvent(gameProgram, "RequestStartMatch", "run measured Udon search");
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_SAMPLE_START preset=" +
                        PresetNames[targetIndex] + " targetVisits=" + Targets[targetIndex] +
                        " revision=" + baselineRevision);
                    SetStep(6);
                    return;
                }

                if (step == 6)
                {
                    int target = Targets[targetIndex];
                    int controllerState = ReadInt(aiProgram, "controllerState");
                    string controllerError = ReadString(aiProgram, "lastError");
                    if (controllerState == GoAiController.STATE_ERROR)
                    {
                        Fail("preset=" + PresetNames[targetIndex] +
                            " controller error=" + controllerError);
                        return;
                    }

                    int phase = ReadInt(searchProgram, "phase");
                    string searchError = ReadString(searchProgram, "lastError");
                    string readerError = ReadString(readerProgram, "lastError");
                    if (phase == GoMctsSearch.PHASE_CANCELLED && searchError != "")
                    {
                        Fail("preset=" + PresetNames[targetIndex] +
                            " search error=" + searchError);
                        return;
                    }
                    if (readerError != "")
                    {
                        Fail("preset=" + PresetNames[targetIndex] +
                            " reader error=" + readerError);
                        return;
                    }

                    int moveCount = ReadInt(gameProgram, "moveCount");
                    int aiMoves = ReadInt(aiProgram, "aiMoves");
                    int visits = ReadInt(searchProgram, "visitsCompleted");
                    int searchTarget = ReadInt(searchProgram, "targetVisits");
                    int completedStages = ReadInt(readerProgram, "completedStages");
                    int ownershipReadbacks = ReadInt(readerProgram, "ownershipReadbackCount");
                    int copiedHistory = ReadInt(searchProgram, "rootHistoryCopiedEntries");
                    int copiedInts = ReadInt(searchProgram, "rootStateCopiedInts");
                    if (ReadBool(gameProgram, "matchStarted") &&
                        moveCount > baselineMoveCount && aiMoves > baselineAiMoves)
                    {
                        if (searchTarget != target || visits < target ||
                            completedStages != 1 || ownershipReadbacks <= baselineOwnershipReadbacks)
                        {
                            Fail("preset=" + PresetNames[targetIndex] +
                                " incomplete search target=" + target +
                                " searchTarget=" + searchTarget +
                                " visits=" + visits +
                                " completedStages=" + completedStages +
                                " ownershipReadbacks=" + ownershipReadbacks +
                                " baselineOwnershipReadbacks=" + baselineOwnershipReadbacks);
                            return;
                        }
                        if(copiedHistory!=baselinePositionHistoryCount||copiedInts>=12000)
                        {
                            Fail("preset="+PresetNames[targetIndex]+
                                " copied a non-bounded root history copiedHistory="+
                                copiedHistory+" expected="+baselinePositionHistoryCount+
                                " copiedInts="+copiedInts+" visits="+visits);
                            return;
                        }

                        double elapsed = EditorApplication.timeSinceStartup - sampleStartSeconds;
                        int frameCount = Time.frameCount - sampleStartFrame;
                        int selectedMove = ReadInt(gameProgram, "lastMove");
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_SAMPLE preset=" +
                            PresetNames[targetIndex] + " targetVisits=" + target +
                            " actualVisits=" + visits + " targetSearchVisits=" + searchTarget +
                            " aiMoves=" + aiMoves + " moveCount=" + moveCount +
                            " elapsedSeconds=" + elapsed.ToString("F3") +
                            " frames=" + frameCount + " selectedMove=" +
                            Coordinate(selectedMove) + " treeNodes=" +
                            ReadInt(searchProgram, "treeNodeCount") + " treeEdges=" +
                            ReadInt(searchProgram, "treeEdgeCount") +
                            " completedStages=" + completedStages +
                            " ownershipReadbacks=" + ownershipReadbacks + " " +
                            " rootStateCopiedInts="+copiedInts+
                            " rootHistoryCopiedEntries="+copiedHistory+
                            " simulationUndoOperations="+
                            ReadInt(searchProgram,"simulationUndoOperations")+" "+
                            DescribeRootStats(searchProgram));

                        if (targetIndex == Targets.Length - 1)
                        {
                            Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=" +
                                GoDifficultyProfile.BEGINNER_VISITS + "," +
                                GoDifficultyProfile.ADVANCED_VISITS + "," +
                                GoDifficultyProfile.MASTER_VISITS + "," +
                                GoDifficultyProfile.ULTRAHARD_VISITS);
                            StopWithResult(true);
                            return;
                        }

                        SendEvent(uiProgram, "ForceReset", "finish measured preset");
                        SetStep(7);
                        return;
                    }
                    FailIfTimedOut("preset=" + PresetNames[targetIndex] +
                        " search did not commit target=" + target +
                        " moveCount=" + moveCount + " aiMoves=" + aiMoves +
                        " visits=" + visits);
                    return;
                }

                if (step == 7)
                {
                    if (ReadBool(gameProgram, "matchStarted") ||
                        ReadInt(gameProgram, "moveCount") != 0)
                        return;
                    targetIndex++;
                    SessionState.SetInt(TargetSessionKey, targetIndex);
                    SetStep(1);
                    return;
                }

                Fail("unknown benchmark state=" + step);
            }
            catch (Exception exception)
            {
                Fail("ClientSim difficulty inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            if (poolProxy == null)
                poolProxy = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if (gameProgram == null)
            {
                GoGame gameProxy = poolProxy != null && poolProxy.tableRoots != null &&
                    poolProxy.tableRoots.Length > 0 && poolProxy.tableRoots[0] != null
                    ? poolProxy.tableRoots[0].GetComponentInChildren<GoGame>(true)
                    : UnityEngine.Object.FindObjectOfType<GoGame>();
                if (gameProxy != null)
                {
                    gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy);
                    if (gameProxy.whiteDifficulty != null)
                        whiteProfileProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(
                            gameProxy.whiteDifficulty);
                }
            }
            if (aiProgram == null)
            {
                GoAiController aiProxy = null;
                if (poolProxy != null && poolProxy.tableRoots != null &&
                    poolProxy.tableRoots.Length > 0 && poolProxy.tableRoots[0] != null)
                    aiProxy = poolProxy.tableRoots[0].GetComponentInChildren<GoAiController>(true);
                if (aiProxy == null && gameProgram != null)
                    aiProxy = ReadReferenceProxy(gameProgram, "aiController") as GoAiController;
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
                GoUI uiProxy = poolProxy == null ? null : poolProxy.primaryUI;
                if (uiProxy == null && poolProxy != null && poolProxy.tableRoots != null &&
                    poolProxy.tableRoots.Length > 0 && poolProxy.tableRoots[0] != null)
                    uiProxy = poolProxy.tableRoots[0].GetComponentInChildren<GoUI>(true);
                if (uiProxy == null)
                {
                    GoUI[] candidates = UnityEngine.Object.FindObjectsOfType<GoUI>(true);
                    for (int i = 0; i < candidates.Length; i++)
                        if (candidates[i] != null && candidates[i].game != null &&
                            candidates[i].game.aiController != null &&
                            candidates[i].game.aiController.tableIdentity == 0)
                        {
                            uiProxy = candidates[i];
                            break;
                        }
                }
                if (uiProxy != null)
                    uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy);
            }
        }

        private static Component ReadReferenceProxy(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value as Component;
        }

        private static void SendEvent(UdonBehaviour program, string eventName, string reason)
        {
            if (program == null)
                throw new InvalidOperationException(
                    "Cannot send Udon event without a program: " + eventName);
            program.SendCustomEvent(eventName);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_EVENT name=" + eventName +
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
            object value = program.GetProgramVariable(variableName);
            return value == null ? 0f : Convert.ToSingle(value);
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

        private static string DescribeRootStats(UdonBehaviour program)
        {
            int[] firstValues = program.GetProgramVariable("nodeFirstEdge") as int[];
            int[] countValues = program.GetProgramVariable("nodeEdgeCount") as int[];
            int[] visits = program.GetProgramVariable("edgeVisits") as int[];
            if (firstValues == null || countValues == null || visits == null ||
                firstValues.Length < 1 || countValues.Length < 1)
                return "rootStats=unavailable";
            int first = firstValues[0];
            int count = countValues[0];
            if (first < 0 || count < 1 || first + count > visits.Length)
                return "rootStats=unavailable";
            int positive = 0;
            int sum = 0;
            int maximum = 0;
            for (int i = 0; i < count; i++)
            {
                int value = visits[first + i];
                if (value <= 0)
                    continue;
                positive++;
                sum += value;
                if (value > maximum)
                    maximum = value;
            }
            return "rootEdges=" + count + " rootPositive=" + positive +
                " rootVisitSum=" + sum + " rootMaxVisits=" + maximum;
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_LADDER_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
