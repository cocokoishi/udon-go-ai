#if UNITY_EDITOR
using System;
using System.Reflection;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Rendering;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Runs the generated world through the real ClientSim player loop. The
    /// editor side only starts Play Mode, dispatches one Udon custom event and
    /// reads the serialized UdonBehaviour variables for evidence; game and AI
    /// transitions are executed by the compiled Udon programs.
    /// </summary>
    public static class GoClientSimRuntimeVerifier
    {
        // A 19x19 Udon GPU query is intentionally frame-budgeted. In the
        // real ClientSim D3D11 path one default Beginner (8-visit) turn can take
        // more than 45 seconds while Unity is also running the editor and
        // ClientSim player loop. Keep the verifier long enough to observe a
        // complete legal AI turn; this does not change the production budget.
        private const double TimeoutSeconds = 180.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimRuntimeVerifier.Pending";

        private static double deadline;
        private static bool startSent;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool succeeded;
        private static UdonBehaviour poolProgram;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour telemetryProgram;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerProgram;
        private static UdonBehaviour runtimeProgram;
        private static UdonBehaviour encoderProgram;
        private static int baselineRevision;
        private static int baselineMoveCount;
        private static int lastLoggedRevision = -1;
        private static int lastLoggedMoveCount = -1;
        private static int lastLoggedAiMoves = -1;
        private static int lastLoggedSearchVisits = -1;
        private static int lastLoggedControllerState = -1;
        private static int lastLoggedSearchPhase = -1;
        private static int lastLoggedReaderState = -1;
        private static int lastLoggedReaderStage = -1;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Runtime")]
        public static void VerifyClientSimRuntime()
        {
            ResetRunner();
            SessionState.SetBool(PendingSessionKey, true);
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " + Editor.GoWorldGenerator.ScenePath);

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

            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_START scene=" + Editor.GoWorldGenerator.ScenePath +
                " timeoutSeconds=" + TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

[MenuItem("Tools/Pure Udon Go/Tests/Inspect VRC Async GPU API")]
        public static void InspectVrcAsyncGpuApi()
        {
            Debug.Log("PURE_UDON_GO_VRC_ASYNC_API type=" +
                typeof(VRCAsyncGPUReadback).AssemblyQualifiedName);
            MethodInfo[] methods = typeof(VRCAsyncGPUReadback).GetMethods(
                BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
                Debug.Log("PURE_UDON_GO_VRC_ASYNC_METHOD " + methods[i]);
            Debug.Log("PURE_UDON_GO_VRC_ASYNC_REQUEST type=" +
                typeof(VRCAsyncGPUReadbackRequest).AssemblyQualifiedName);
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            startSent = false;
            enteredPlayMode = false;
            resultWritten = false;
            succeeded = false;
            poolProgram = null;
            uiProgram = null;
            telemetryProgram = null;
            gameProgram = null;
            aiProgram = null;
            searchProgram = null;
            readerProgram = null;
            runtimeProgram = null;
            encoderProgram = null;
            baselineRevision = 0;
            baselineMoveCount = 0;
            lastLoggedRevision = -1;
            lastLoggedMoveCount = -1;
            lastLoggedAiMoves = -1;
            lastLoggedSearchVisits = -1;
            lastLoggedControllerState = -1;
            lastLoggedSearchPhase = -1;
            lastLoggedReaderState = -1;
            lastLoggedReaderStage = -1;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before the Udon runtime result was recorded");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }

                if (poolProgram == null || uiProgram == null)
                {
                    GoBoardPool poolProxy = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
                    if (poolProxy != null)
                    {
                        poolProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(poolProxy);
                        GoUI primaryUi = poolProxy.primaryUI;
                        if (primaryUi != null)
                            uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(primaryUi);
                    }
                }

                if (gameProgram == null)
                {
                    GoGame gameProxy = null;
                    gameProxy = uiProgram == null ? null :
                        uiProgram.GetProgramVariable("game") as GoGame;
                    if (gameProxy == null)
                        gameProxy = UnityEngine.Object.FindObjectOfType<GoGame>();
                    if (gameProxy != null)
                        gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy);
                }

                if (aiProgram == null)
                {
                    GoAiController aiProxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
                    if (aiProxy != null)
                        aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(aiProxy);
                }

                if (gameProgram == null || aiProgram == null)
                {
                    FailIfTimedOut("Generated Go UdonBehaviour proxies were not found");
                    return;
                }

                if (telemetryProgram == null)
                    telemetryProgram = ReadReference(gameProgram, "telemetry");

                int revision = ReadInt(gameProgram, "revision");
                int moveCount = ReadInt(gameProgram, "moveCount");
                if (!startSent)
                {
                    baselineRevision = revision;
                    baselineMoveCount = moveCount;
                    readerProgram = ReadReference(aiProgram, "reader");
                    runtimeProgram = ReadReference(aiProgram, "runtime");
                    searchProgram = ReadReference(aiProgram, "search");
                    encoderProgram = ReadReference(aiProgram, "encoder");
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_AI_CONFIG autoStart=" +
                        ReadBool(aiProgram, "autoStart") +
                        " mode=" + ReadInt(aiProgram, "mode") +
                        " aiColor=" + ReadInt(aiProgram, "aiColor") +
                        " aiEnabled=" + aiProgram.enabled +
                        " aiActive=" + aiProgram.gameObject.activeInHierarchy +
                        " gameRef=" + DescribeReference(aiProgram, "game") +
                        " difficultyRef=" + DescribeReference(aiProgram, "difficulty") +
                        " encoderRef=" + DescribeReference(aiProgram, "encoder") +
                        " runtimeRef=" + DescribeReference(aiProgram, "runtime") +
                        " readerRef=" + DescribeReference(aiProgram, "reader") +
                        " searchRef=" + DescribeReference(aiProgram, "search") +
                        " hasError=" + aiProgram.HasError +
                        " events=" + DescribeEvents(aiProgram) +
                        " readerEvents=" + DescribeEvents(readerProgram) +
                        " runtimeEvents=" + DescribeEvents(runtimeProgram) +
                        " searchEvents=" + DescribeEvents(searchProgram));
                    gameProgram.SendCustomEvent("SetWhiteStarts");
                    gameProgram.SendCustomEvent("RequestStartMatch");
                    startSent = true;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_UDON_EVENT events=SetWhiteStarts,RequestStartMatch" +
                        " baselineRevision=" + baselineRevision +
                        " baselineMoveCount=" + baselineMoveCount);
                    return;
                }

                bool matchStarted = ReadBool(gameProgram, "matchStarted");
                int aiMoves = ReadInt(aiProgram, "aiMoves");
                int controllerState = ReadInt(aiProgram, "controllerState");
                int aiErrorState = GoAiController.STATE_ERROR;
                string lastError = ReadString(aiProgram, "lastError");
                if (searchProgram == null)
                {
                    GoMctsSearch searchProxy = UnityEngine.Object.FindObjectOfType<GoMctsSearch>();
                    if (searchProxy != null)
                        searchProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(searchProxy);
                }
                int searchVisits = searchProgram == null ? 0 :
                    ReadInt(searchProgram, "visitsCompleted");
                int searchPhase = searchProgram == null ? -1 : ReadInt(searchProgram, "phase");
                bool resourceWarmupActive=ReadBool(aiProgram, "resourceWarmupActive");
                bool resourceWarmupComplete=ReadBool(aiProgram, "resourceWarmupComplete");
                int readerState = readerProgram == null ? -1 :
                    ReadInt(readerProgram, "readbackState");
                int readerStage = readerProgram == null ? -1 :
                    ReadInt(readerProgram, "readbackStage");
                int readerCompletedStages = readerProgram == null ? -1 :
                    ReadInt(readerProgram, "completedStages");
                string readerError = readerProgram == null ? "" :
                    ReadString(readerProgram, "lastError");
                string runtimeError = runtimeProgram == null ? "" :
                    ReadString(runtimeProgram, "lastError");

                // A real search must not start until the owner has exercised
                // the production GPU graph and one packed readback with the
                // match-start dummy warm-up.  This protects the regression
                // gate from silently falling back to allocation-only warm-up.
                if(resourceWarmupActive&&searchPhase!=GoMctsSearch.PHASE_IDLE&&
                    searchPhase!=GoMctsSearch.PHASE_CANCELLED)
                {
                    Fail("AI search started before pipeline warm-up completed");
                    return;
                }

                if (matchStarted && (revision != baselineRevision || moveCount > baselineMoveCount) &&
                    (revision != lastLoggedRevision || moveCount != lastLoggedMoveCount ||
                     aiMoves != lastLoggedAiMoves || searchVisits != lastLoggedSearchVisits ||
                     controllerState != lastLoggedControllerState ||
                     searchPhase != lastLoggedSearchPhase || readerState != lastLoggedReaderState ||
                     readerStage != lastLoggedReaderStage))
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_STATE matchStarted=" + matchStarted +
                        " revision=" + revision + " moveCount=" + moveCount +
                        " aiMoves=" + aiMoves + " searchVisits=" + searchVisits +
                        " controllerState=" + controllerState + " searchPhase=" + searchPhase +
                        " readerState=" + readerState + " readerStage=" + readerStage +
                        " readerCompletedStages=" + readerCompletedStages +
                        " readerError=" + readerError + " runtimeError=" + runtimeError);
                    lastLoggedRevision = revision;
                    lastLoggedMoveCount = moveCount;
                    lastLoggedAiMoves = aiMoves;
                    lastLoggedSearchVisits = searchVisits;
                    lastLoggedControllerState = controllerState;
                    lastLoggedSearchPhase = searchPhase;
                    lastLoggedReaderState = readerState;
                    lastLoggedReaderStage = readerStage;
                }

                if (aiMoves > 0 && moveCount > baselineMoveCount && searchVisits > 0)
                {
                    if(!resourceWarmupComplete||
                        !ReadBool(aiProgram,"pipelineWarmupReadbackComplete"))
                    {
                        Fail("AI move completed without a verified match-start pipeline warm-up");
                        return;
                    }
                    int selectedMove = ReadInt(gameProgram, "lastMove");
                    int searchBestMove = searchProgram == null ? GoGame.NONE :
                        ReadInt(searchProgram, "bestMove");
                    int encodeGeneration = encoderProgram == null ? 0 :
                        ReadInt(encoderProgram, "encodeGeneration");
                    // The root neural evaluation is the first completed
                    // visit, so each completed visit owns exactly one fresh
                    // feature encode. There is no hidden extra root encode.
                    int expectedEncodes = searchVisits;
                    if (encoderProgram == null || encodeGeneration < expectedEncodes)
                    {
                        Fail("AI committed after stale feature reuse: encodeGeneration=" +
                            encodeGeneration + " expectedAtLeast=" + expectedEncodes +
                            " visits=" + searchVisits);
                        return;
                    }
                    succeeded = true;
                    resultWritten = true;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PASS matchStarted=" + matchStarted +
                        " revision=" + revision + " moveCount=" + moveCount +
                        " aiMoves=" + aiMoves + " searchVisits=" + searchVisits +
                        " controllerState=" + controllerState +
                        " lastMoveLoc=" + selectedMove +
                        " lastMove=" + Coordinate(selectedMove) +
                        " searchBestMove=" + Coordinate(searchBestMove) +
                        " encodeGeneration=" + encodeGeneration +
                        " expectedEncodesAtLeast=" + expectedEncodes +
                        " resourceWarmupComplete=" + resourceWarmupComplete +
                        " pipelineWarmupReadbackComplete=" +
                        ReadBool(aiProgram,"pipelineWarmupReadbackComplete") +
                        " " + DescribeRootSearch(searchProgram));
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF " + DescribePerformance());
                    StopWithResult(true);
                    return;
                }

                if (controllerState == aiErrorState)
                {
                    Fail("AI Udon controller entered error state: " + lastError);
                    return;
                }

                FailIfTimedOut("Udon match/AI did not advance" +
                    " matchStarted=" + matchStarted +
                    " revision=" + revision + " moveCount=" + moveCount +
                    " aiMoves=" + aiMoves + " searchVisits=" + searchVisits);
            }
            catch (Exception exception)
            {
                Fail("ClientSim inspection exception: " + exception);
            }
        }

        private static int ReadInt(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static bool ReadBool(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static float ReadFloat(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static string ReadString(UdonBehaviour program, string variableName)
        {
            object value = program == null ? null : program.GetProgramVariable(variableName);
            return value == null ? "" : value.ToString();
        }

        private static string DescribeReference(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            if (value == null)
                return "null";
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return unityObject == null
                ? value.GetType().Name
                : value.GetType().Name + ":" + unityObject.name;
        }

        private static UdonBehaviour ReadReference(UdonBehaviour program, string variableName)
        {
            return program.GetProgramVariable(variableName) as UdonBehaviour;
        }

        private static string DescribePerformance()
        {
            return "targetVisits=" + ReadInt(aiProgram, "lastSearchTargetVisits") +
                " completedVisits=" + ReadInt(aiProgram, "lastSearchCompletedVisits") +
                " frames=" + ReadInt(aiProgram, "lastSearchFrames") +
                " totalMs=" + ReadFloat(aiProgram, "lastSearchMilliseconds").ToString("F3") +
                " setupMs=" + ReadFloat(aiProgram, "lastSearchSetupMilliseconds").ToString("F3") +
                " searchStepMs=" + ReadFloat(aiProgram, "lastSearchStepMilliseconds").ToString("F3") +
                " superkoMs=" + ReadFloat(aiProgram, "lastSuperkoMilliseconds").ToString("F3") +
                " featureMs=" + ReadFloat(aiProgram, "lastFeatureMilliseconds").ToString("F3") +
                " gpuSetupMs=" + ReadFloat(aiProgram, "lastGpuInputSetupMilliseconds").ToString("F3") +
                " gpuUploadMs=" + ReadFloat(aiProgram, "lastGpuUploadMilliseconds").ToString("F3") +
                " gpuDispatchMs=" + ReadFloat(aiProgram, "lastGpuDispatchMilliseconds").ToString("F3") +
                " gpuEvalMs=" + ReadFloat(aiProgram, "lastGpuEvaluationMilliseconds").ToString("F3") +
                " readbackMs=" + ReadFloat(aiProgram, "lastReadbackMilliseconds").ToString("F3") +
                " neuralSubmitMs=" + ReadFloat(aiProgram, "lastNeuralSubmitMilliseconds").ToString("F3") +
                " commitMs=" + ReadFloat(aiProgram, "lastCommitMilliseconds").ToString("F3") +
                " featureSteps=" + ReadInt(aiProgram, "lastFeatureSteps") +
                " ladderStepCalls=" + ReadInt(aiProgram, "lastLadderStepCalls") +
                " ladderTransitions=" + ReadInt(aiProgram, "lastLadderSearchTransitions") +
                " ladderNodes=" + ReadInt(aiProgram, "lastLadderNodes") +
                " gpuPasses=" + ReadInt(aiProgram, "lastGpuPasses") +
                " readbackRequests=" + ReadInt(aiProgram, "lastReadbackRequests") +
                " readbackStages=" + ReadInt(aiProgram, "lastReadbackStages") +
                " rootCopyMs=" + ReadFloat(aiProgram, "lastRootCopyMilliseconds").ToString("F3") +
                " expansionMs=" + ReadFloat(aiProgram, "lastNeuralExpansionMilliseconds").ToString("F3") +
                " backupMs=" + ReadFloat(aiProgram, "lastBackupMilliseconds").ToString("F3") +
                " legalChecks=" + ReadInt(aiProgram, "lastLegalMoveChecks") +
                " simulationPlays=" + ReadInt(aiProgram, "lastSimulationPlayCalls") +
                " encoderGeneration=" + ReadInt(encoderProgram, "encodeGeneration") +
                " uiRefreshCount=" + ReadInt(uiProgram, "refreshCount") +
                " telemetryRefreshCount=" + ReadInt(telemetryProgram, "refreshCount") +
                " schedulerObservedFps=" + ReadFloat(aiProgram, "schedulerObservedFps").ToString("F1") +
                " schedulerStride=" + ReadInt(aiProgram, "schedulerStride") +
                " schedulerDispatchCount=" + ReadInt(aiProgram, "schedulerDispatchCount") +
                " schedulerSkipCount=" + ReadInt(aiProgram, "schedulerSkipCount") +
                " warmupFrames=" + ReadInt(aiProgram, "pipelineWarmupFrames") +
                " warmupPasses=" + ReadInt(aiProgram, "pipelineWarmupPasses") +
                " warmupMs=" + ReadFloat(aiProgram, "pipelineWarmupMilliseconds").ToString("F3") +
                " warmupReadback=" + ReadBool(aiProgram, "pipelineWarmupReadbackComplete");
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

        private static string DescribeRootSearch(UdonBehaviour program)
        {
            if (program == null)
                return "rootSearch=null";
            int[] firstValues = program.GetProgramVariable("nodeFirstEdge") as int[];
            int[] countValues = program.GetProgramVariable("nodeEdgeCount") as int[];
            int[] moves = program.GetProgramVariable("edgeMove") as int[];
            int[] visits = program.GetProgramVariable("edgeVisits") as int[];
            float[] priors = program.GetProgramVariable("edgePrior") as float[];
            if (firstValues == null || countValues == null || firstValues.Length < 1 ||
                countValues.Length < 1 || moves == null || visits == null)
                return "rootSearch=unavailable";
            int first = firstValues[0];
            int count = countValues[0];
            if (first < 0 || count < 1 || first + count > moves.Length ||
                first + count > visits.Length)
                return "rootSearch=unavailable";

            int positive = 0;
            int sum = 0;
            int maximum = 0;
            int[] topEdges = new int[6];
            for (int i = 0; i < topEdges.Length; i++) topEdges[i] = -1;
            for (int i = 0; i < count; i++)
            {
                int edge = first + i;
                int value = visits[edge];
                if (value > 0)
                {
                    positive++;
                    sum += value;
                    if (value > maximum) maximum = value;
                }
                if (value <= 0)
                    continue;
                int insert = 0;
                while (insert < topEdges.Length && topEdges[insert] >= 0 &&
                    BetterRootEdge(edge, topEdges[insert], visits, priors)) insert++;
                if (insert >= topEdges.Length) continue;
                for (int j = topEdges.Length - 1; j > insert; j--)
                    topEdges[j] = topEdges[j - 1];
                topEdges[insert] = edge;
            }

            string top = "";
            for (int i = 0; i < topEdges.Length; i++)
            {
                if (topEdges[i] < 0) continue;
                if (top.Length > 0) top += ",";
                int edge = topEdges[i];
                float prior = priors != null && edge < priors.Length ? priors[edge] : 0f;
                top += Coordinate(moves[edge]) + ":" + visits[edge] + ":" +
                    prior.ToString("F6");
            }
            return "rootEdges=" + count + " rootPositive=" + positive +
                " rootVisitSum=" + sum + " rootMaxVisits=" + maximum +
                " rootTop=" + top;
        }

        private static bool BetterRootEdge(int left, int right, int[] visits, float[] priors)
        {
            if (visits[left] != visits[right]) return visits[left] > visits[right];
            float leftPrior = priors != null && left < priors.Length ? priors[left] : 0f;
            float rightPrior = priors != null && right < priors.Length ? priors[right] : 0f;
            return leftPrior > rightPrior;
        }

        private static string DescribeEvents(UdonBehaviour program)
        {
            if (program == null)
                return "null";
            bool update = false;
            bool start = false;
            var events = program.GetPrograms();
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i] == "_update") update = true;
                if (events[i] == "_start") start = true;
            }
            string names = "";
            for (int i = 0; i < events.Length; i++)
            {
                if (i > 0) names += ",";
                names += events[i];
            }
            return "start=" + start + ",update=" + update + ",count=" + events.Length +
                ",names=" + names;
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
            succeeded = false;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            succeeded = pass;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RESULT pass=" + pass);
            EditorApplication.Exit(succeeded ? 0 : 1);
        }
    }
}
#endif
