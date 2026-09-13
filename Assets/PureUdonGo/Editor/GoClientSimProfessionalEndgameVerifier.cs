#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
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
    /// Runs the checked-in 32-position professional endgame corpus through a
    /// real one-table Udon/ClientSim fixture.  KataGo oracle comparison is a
    /// separate command-line step; this verifier only records authoritative
    /// Udon observations and exact visit invariants.
    /// </summary>
    public static class GoClientSimProfessionalEndgameVerifier
    {
        private const double DefaultTimeoutSeconds = 1200.0;
        private const string PendingKey =
            "PureUdonGo.ClientSimProfessionalEndgameVerifier.Pending";
        private const string ReferencePendingKey =
            "PureUdonGo.ClientSimProfessionalEndgameVerifier.ReferencePending";
        private const string ManifestRelativePath =
            "udon-go-ai/Benchmarks/KaTrain/EndgameRegression/professional-endgames.json";
        private const string OutputRelativePath =
            "udon-go-ai/Benchmarks/KaTrain/ClientSimExports/GoProfessionalEndgame/go-professional-endgame.json";
        private const string ReferenceInputRelativePath =
            "udon-go-ai/Benchmarks/KaTrain/ClientSimExports/GoProfessionalEndgame/udon-search-reference-input.json";

        private static double deadline;
        private static double timeoutSeconds = DefaultTimeoutSeconds;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;
        private static UdonBehaviour searchProgram;
        private static Manifest manifest;
        private static bool captureReferenceTraceForRun;
        private static bool referenceTraceActive;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingKey, false))
                return;
            manifest = LoadManifest();
            referenceTraceActive = SessionState.GetBool(ReferencePendingKey, false);
            timeoutSeconds = ReadEnvironmentDouble(
                "PURE_UDON_GO_ENDGAME_TIMEOUT_SECONDS", DefaultTimeoutSeconds);
            deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Professional Endgame Consistency")]
        public static void VerifyProfessionalEndgameConsistency()
        {
            StartRun(0, GoProfessionalEndgameProbe.POSITION_COUNT, 0,
                GoProfessionalEndgameProbe.BUDGET_COUNT);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Professional Endgame Smoke (1x4)")]
        public static void VerifyProfessionalEndgameSmoke()
        {
            StartRun(0, 1, 0, 1);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Professional Endgame 8 Visits")]
        public static void VerifyProfessionalEndgame8Visits()
        {
            StartRun(0, GoProfessionalEndgameProbe.POSITION_COUNT, 0, 1);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Professional Endgame 8 Visits (root trace)")]
        public static void VerifyProfessionalEndgame8VisitsTrace()
        {
            captureReferenceTraceForRun = true;
            StartRun(0, GoProfessionalEndgameProbe.POSITION_COUNT, 0, 1);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Professional Endgame 48 Visits")]
        public static void VerifyProfessionalEndgame48Visits()
        {
            StartRun(0, GoProfessionalEndgameProbe.POSITION_COUNT, 1, 2);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Professional Endgame 48 Visits (root trace)")]
        public static void VerifyProfessionalEndgame48VisitsTrace()
        {
            captureReferenceTraceForRun = true;
            StartRun(0, GoProfessionalEndgameProbe.POSITION_COUNT, 1, 2);
        }
[MenuItem("Tools/Pure Udon Go/Tests/Verify Udon Search Reference (8 visits)")]
        public static void VerifyUdonSearchReference()
        {
            captureReferenceTraceForRun = true;
            StartRun(0, 1, 0, 1);
        }

        private static void StartRun(int positionStart, int positionLimit,
            int budgetStart, int budgetLimit)
        {
            ResetRunner();
            manifest = LoadManifest();
            ValidateManifest(manifest);
            timeoutSeconds = ReadEnvironmentDouble(
                "PURE_UDON_GO_ENDGAME_TIMEOUT_SECONDS", DefaultTimeoutSeconds);

            // Reuse the already generated minimal one-table fixture when it is
            // present. Rebuilding it on every run forces an AssetDatabase
            // refresh of every production Udon program and can exhaust the
            // Unity editor before the probe starts. Production generation is
            // validated by its own gate; this verifier only needs the real
            // runtime graph and compiles the injected probe below.
            string fixtureAbsolutePath = Path.Combine(
                Application.dataPath, "PureUdonGo/Generated/Scenes/",
                Path.GetFileName(Editor.GoWorldGenerator.ReaderRecoveryScenePath));
            if (!File.Exists(fixtureAbsolutePath))
                Editor.GoWorldGenerator.GenerateReaderRecoveryFixtureScene();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ReaderRecoveryScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Professional endgame fixture scene was not loaded");

            GoGeneratedWorld root = UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoGame game = UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller = UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (game == null || controller == null || game.blackDifficulty == null ||
                game.whiteDifficulty == null)
                throw new InvalidOperationException(
                    "Professional endgame fixture runtime objects were not found");
            searchProgram = controller.search == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.search);
            referenceTraceActive = captureReferenceTraceForRun;

            string programPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoProfessionalEndgameProbe), "Professional-endgame");
            GameObject probeObject = new GameObject(
                "ClientSim Professional Endgame Consistency Probe");
            probeObject.transform.SetParent(
                root != null ? root.transform : game.transform, false);
            GoProfessionalEndgameProbe probe =
                probeObject.AddUdonSharpComponent<GoProfessionalEndgameProbe>();
            probe.game = game;
            probe.controller = controller;
            probe.blackDifficulty = game.blackDifficulty;
            probe.whiteDifficulty = game.whiteDifficulty;
            probe.aiSettings = game.aiSettings;
            probe.positionStart = positionStart;
            probe.positionLimit = positionLimit;
            probe.budgetStart = budgetStart;
            probe.budgetLimit = budgetLimit;
            probe.captureReferenceTrace = captureReferenceTraceForRun;
            probe.referenceTraceCapacity = GoProfessionalEndgameProbe.DECISION_TRACE_CAPACITY;
            probe.searchSemanticsMode = ReadEnvironmentInt(
                "PURE_UDON_GO_SEARCH_SEMANTICS", 0);
            SessionState.SetBool(ReferencePendingKey, captureReferenceTraceForRun);
            captureReferenceTraceForRun = false;
            probe.positionIds = new string[GoProfessionalEndgameProbe.POSITION_COUNT];
            probe.positionMoveOffsets = new int[GoProfessionalEndgameProbe.POSITION_COUNT];
            probe.positionMoveCounts = new int[GoProfessionalEndgameProbe.POSITION_COUNT];

            List<int> flattened = new List<int>();
            for (int i = 0; i < manifest.positions.Length; i++)
            {
                ManifestPosition position = manifest.positions[i];
                probe.positionIds[i] = position.id;
                probe.positionMoveOffsets[i] = flattened.Count;
                probe.positionMoveCounts[i] = position.moves.Length;
                flattened.AddRange(position.moves);
            }
            probe.flattenedMoves = flattened.ToArray();
            GoClientSimProbeAssetUtility.CompileAndCopy(probe, programPath);

            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim = true,
                // Keep ClientSim's per-frame diagnostic chatter out of the
                // benchmark log. The verifier still emits its own gate lines
                // and reads all probe variables through Udon.
                displayLogs = false,
                deleteEditorOnly = false,
                spawnPlayer = true,
                hideMenuOnLaunch = true,
                setTargetFrameRate = false,
                localPlayerIsMaster = true,
                isInstanceOwner = true,
                initializationDelay = 0f,
                currentLanguage = "en"
            });

            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PRO_ENDGAME_START positions=" +
                (positionLimit - positionStart) + " positions budgets=" +
                string.Join(",", manifest.budgets) +
                " samples=" + ((positionLimit - positionStart) *
                    (budgetLimit - budgetStart)) +
                " timeoutSeconds=" + timeoutSeconds +
                " fixture=" + Editor.GoWorldGenerator.ReaderRecoveryScenePath);
            EditorApplication.isPlaying = true;
        }

        private static int ReadEnvironmentInt(string name, int fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static double ReadEnvironmentDouble(string name, double fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            double parsed;
            return double.TryParse(value, out parsed) && parsed > 0.0 ? parsed : fallback;
        }

        private static string WriteReferenceInput()
        {
            int expansionCount = ReadSearchInt("referenceTraceExpansionCount");
            int[] nodeIds = ReadSearchIntArray("referenceTraceNodeIds");
            int[] parentIds = ReadSearchIntArray("referenceTraceExpansionParentNodeIds");
            int[] parentMoves = ReadSearchIntArray("referenceTraceExpansionParentMoves");
            int[] legalMask = ReadSearchIntArray("referenceTraceLegalMask");
            float[] policySpatial = ReadSearchFloatArray("referenceTracePolicySpatial");
            float[] policyPass = ReadSearchFloatArray("referenceTracePolicyPass");
            float[] value0 = ReadSearchFloatArray("referenceTraceValue0");
            float[] value1 = ReadSearchFloatArray("referenceTraceValue1");
            float[] value2 = ReadSearchFloatArray("referenceTraceValue2");
            float[] scoreMean = ReadSearchFloatArray("referenceTraceScoreMean");
            float[] scoreStdev = ReadSearchFloatArray("referenceTraceScoreStdev");
            if (expansionCount < 1 || nodeIds == null || parentIds == null ||
                parentMoves == null || legalMask == null || policySpatial == null ||
                policyPass == null || value0 == null || value1 == null ||
                value2 == null || scoreMean == null || scoreStdev == null)
                throw new InvalidOperationException("Udon reference trace arrays are incomplete");

            ReferenceExpansion[] expansions = new ReferenceExpansion[expansionCount];
            for (int i = 0; i < expansionCount; i++)
            {
                List<int> legal = new List<int>();
                int maskBase = i * (GoGame.AREA + 1);
                for (int loc = 0; loc <= GoGame.AREA; loc++)
                {
                    if (maskBase + loc < legalMask.Length && legalMask[maskBase + loc] != 0)
                        legal.Add(loc);
                }
                float[] policy = new float[GoGame.AREA];
                int policyBase = i * GoGame.AREA;
                for (int loc = 0; loc < GoGame.AREA; loc++)
                    policy[loc] = policyBase + loc < policySpatial.Length ?
                        policySpatial[policyBase + loc] : 0f;
                expansions[i] = new ReferenceExpansion
                {
                    nodeId = i < nodeIds.Length ? nodeIds[i] : -1,
                    parentNodeId = i < parentIds.Length ? parentIds[i] : -1,
                    parentMove = i < parentMoves.Length ? parentMoves[i] : GoGame.NONE,
                    legalMoves = legal.ToArray(),
                    policySpatialLogits = policy,
                    policyPassLogit = i < policyPass.Length ? policyPass[i] : 0f,
                    valueLogit0 = i < value0.Length ? value0[i] : 0f,
                    valueLogit1 = i < value1.Length ? value1[i] : 0f,
                    valueLogit2 = i < value2.Length ? value2[i] : 0f,
                    scoreMean = i < scoreMean.Length ? scoreMean[i] : 0f,
                    scoreStdev = i < scoreStdev.Length ? scoreStdev[i] : 0f
                };
            }

            int[] rootFirstEdges = ReadSearchIntArray("nodeFirstEdge");
            int[] rootEdgeCounts = ReadSearchIntArray("nodeEdgeCount");
            int[] edgeMoves = ReadSearchIntArray("edgeMove");
            int[] edgeVisits = ReadSearchIntArray("edgeVisits");
            float[] edgePriors = ReadSearchFloatArray("edgePrior");
            float[] edgeValues = ReadSearchFloatArray("edgeValueSum");
            int firstEdge = rootFirstEdges == null || rootFirstEdges.Length == 0 ? 0 :
                rootFirstEdges[0];
            int edgeCount = rootEdgeCounts == null || rootEdgeCounts.Length == 0 ? 0 :
                rootEdgeCounts[0];
            ReferenceEdge[] expectedEdges = new ReferenceEdge[Mathf.Max(0, edgeCount)];
            for (int i = 0; i < expectedEdges.Length; i++)
            {
                int edge = firstEdge + i;
                expectedEdges[i] = new ReferenceEdge
                {
                    move = edgeMoves != null && edge < edgeMoves.Length ? edgeMoves[edge] : GoGame.NONE,
                    prior = edgePriors != null && edge < edgePriors.Length ? edgePriors[edge] : 0f,
                    visits = edgeVisits != null && edge < edgeVisits.Length ? edgeVisits[edge] : 0,
                    valueSum = edgeValues != null && edge < edgeValues.Length ? edgeValues[edge] : 0f
                };
            }
            int[] deterministic = ReadProbeIntArray("deterministicRootMoves");
            int[] targets = ReadProbeIntArray("targetSearchVisits");
            ReferenceInput input = new ReferenceInput
            {
                schema = "pure-udon-go.udon-search-reference-input.v2",
                targetVisits = targets == null || targets.Length == 0 ? 0 : targets[0],
                explorationConstant = ReadSearchFloat("explorationConstant"),
                searchSemanticsMode = ReadSearchInt("searchSemanticsMode"),
                positionalSuperko = true,
                areaScoring = true,
                rootMoveTemperature = ReadSearchFloat("rootMoveTemperature"),
                rootPolicyTopK = ReadSearchInt("rootPolicyTopK"),
                rootSelectionSeed = ReadSearchInt("rootSelectionSeed"),
                expansions = expansions,
                expectedRootVisits = ReadSearchIntArray("nodeVisits") == null ? 0 :
                    ReadSearchIntArray("nodeVisits")[0],
                expectedDeterministicRootMove = deterministic == null || deterministic.Length == 0 ?
                    GoGame.NONE : deterministic[0],
                expectedRootEdges = expectedEdges
            };
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, "Assets", ReferenceInputRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(input, true));
            return path;
        }

        private static int ReadSearchInt(string name)
        {
            if (searchProgram == null) return 0;
            object value = searchProgram.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static float ReadSearchFloat(string name)
        {
            if (searchProgram == null) return 0f;
            object value = searchProgram.GetProgramVariable(name);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static int[] ReadSearchIntArray(string name)
        { return searchProgram == null ? null : searchProgram.GetProgramVariable(name) as int[]; }

        private static float[] ReadSearchFloatArray(string name)
        { return searchProgram == null ? null : searchProgram.GetProgramVariable(name) as float[]; }

        private static int[] ReadProbeIntArray(string name)
        { return probeProgram == null ? null : probeProgram.GetProgramVariable(name) as int[]; }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            SessionState.SetBool(PendingKey, false);
            SessionState.SetBool(ReferencePendingKey, false);
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            probeSent = false;
            probeProgram = null;
            searchProgram = null;
            referenceTraceActive = false;
            manifest = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PRO_ENDGAME_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                SessionState.GetBool(PendingKey, false))
            {
                Fail("play mode ended before professional endgame corpus completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
                enteredPlayMode = true;
            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                if (probeProgram == null)
                {
                    GoProfessionalEndgameProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoProfessionalEndgameProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (searchProgram == null)
                {
                    GoAiController searchController =
                        UnityEngine.Object.FindObjectOfType<GoAiController>();
                    if (searchController != null && searchController.search != null)
                        searchProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(
                            searchController.search);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("professional endgame probe was not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PRO_ENDGAME_EVENT name=RunProfessionalEndgameProbe");
                    probeProgram.SendCustomEvent("RunProfessionalEndgameProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("professional endgame probe did not finish");
                    return;
                }
                bool passed = ReadBool("probePassed");
                string failure = ReadString("failure");
                string output = WriteTrace(passed, failure);
                if (referenceTraceActive && searchProgram != null)
                {
                    string referenceOutput = WriteReferenceInput();
                    Debug.Log("PURE_UDON_GO_SEARCH_REFERENCE_INPUT " + referenceOutput);
                    referenceTraceActive = false;
                    SessionState.SetBool(ReferencePendingKey, false);
                }
                if (!passed)
                {
                    Fail("Udon professional endgame probe failed: " + failure);
                    return;
                }
            if (!HasExpectedArrays())
            {
                Fail("professional endgame probe arrays are incomplete");
                return;
                }
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PRO_ENDGAME_PASS positions=" +
                    (ReadInt("positionLimit") - ReadInt("positionStart")) +
                    " budgets=" +
                    string.Join(",", manifest.budgets) +
                    " samples=" + ExpectedSampleCount() +
                    " output=" + output);
                StopWithResult(true);
            }
            catch (Exception exception)
            {
                Fail("professional endgame inspection exception: " + exception);
            }
        }

        private static bool HasExpectedArrays()
        {
            int count = ExpectedSampleCount();
            int[] actual = ReadIntArray("actualVisits");
            int[] target = ReadIntArray("targetSearchVisits");
            int[] selected = ReadIntArray("selectedMoves");
            int[] sampledSelected = ReadIntArray("sampledSelectedMoves");
            int[] deterministicRoot = ReadIntArray("deterministicRootMoves");
            int[] deterministicVisits = ReadIntArray("deterministicRootVisits");
            int[] rootVisits = ReadIntArray("selectedRootVisits");
            int[] stages = ReadIntArray("readerStages");
            int[] ownership = ReadIntArray("ownershipReadbacks");
            int[] revisions = ReadIntArray("rootOutputRevisions");
            int[] tokens = ReadIntArray("rootOutputSearchTokens");
            int[] candidateMoves = ReadIntArray("rootCandidateMoves");
            int[] candidateVisits = ReadIntArray("rootCandidateVisits");
            if (actual == null || target == null || selected == null ||
                sampledSelected == null || deterministicRoot == null ||
                deterministicVisits == null ||
                rootVisits == null || stages == null || ownership == null ||
                revisions == null || tokens == null || actual.Length < count ||
                target.Length < count || selected.Length < count ||
                rootVisits.Length < count || stages.Length < count ||
                ownership.Length < count || revisions.Length < count ||
                tokens.Length < count || candidateMoves == null ||
                candidateVisits == null || candidateMoves.Length <
                    count * GoProfessionalEndgameProbe.ROOT_CANDIDATE_COUNT ||
                candidateVisits.Length <
                    count * GoProfessionalEndgameProbe.ROOT_CANDIDATE_COUNT)
                return false;
            int runPositionCount = ReadInt("positionLimit") - ReadInt("positionStart");
            for (int i = 0; i < count; i++)
            {
                int budget = ReadInt("budgetStart") + i / runPositionCount;
                int expected = manifest.budgets[budget];
                if (actual[i] != expected || target[i] != expected ||
                    selected[i] == GoGame.NONE || sampledSelected[i] == GoGame.NONE ||
                    deterministicRoot[i] == GoGame.NONE || deterministicVisits[i] <= 0 ||
                    rootVisits[i] <= 0 ||
                    stages[i] < 1 || ownership[i] <= 0 || revisions[i] <= 0 ||
                    tokens[i] <= 0)
                    return false;
            }
            if (ReadBool("captureReferenceTrace"))
            {
                int[] decisionCounts = ReadIntArray("referenceDecisionCounts");
                if (decisionCounts == null || decisionCounts.Length < count)
                    return false;
                for (int i = 0; i < count; i++)
                {
                    // The first root neural expansion is visit 0; each
                    // subsequent visit contributes one root selection record.
                    int expectedDecisions = Mathf.Max(0, target[i] - 1);
                    if (decisionCounts[i] < expectedDecisions)
                        return false;
                }
            }
            return true;
        }

        private static string WriteTrace(bool passed, string failure)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            int budgetStart = ReadInt("budgetStart");
            int budgetLimit = ReadInt("budgetLimit");
            string outputRelativePath = OutputRelativePath;
            if (budgetStart != 0 || budgetLimit != GoProfessionalEndgameProbe.BUDGET_COUNT)
            {
                string stem = Path.GetFileNameWithoutExtension(OutputRelativePath);
                string extension = Path.GetExtension(OutputRelativePath);
                outputRelativePath = Path.Combine(
                    Path.GetDirectoryName(OutputRelativePath),
                    stem + "-v" + manifest.budgets[budgetStart] + extension);
            }
            int positionStart = ReadInt("positionStart");
            int positionLimit = ReadInt("positionLimit");
            if (positionStart != 0 || positionLimit != GoProfessionalEndgameProbe.POSITION_COUNT)
            {
                string stem = Path.GetFileNameWithoutExtension(outputRelativePath);
                string extension = Path.GetExtension(outputRelativePath);
                outputRelativePath = Path.Combine(
                    Path.GetDirectoryName(outputRelativePath),
                    stem + "-p" + positionStart + "-" + positionLimit + extension);
            }
            string path = Path.Combine(projectRoot, "Assets", outputRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Trace trace = new Trace
            {
                // v4 adds the bounded root-selection decision tape while
                // preserving all v3 row fields and watchdog counters.
                schema = "pure-udon-go.clientsim-professional-endgame.v4",
                status = passed ? "CLIENTSIM_PRO_ENDGAME_PASS" : "CLIENTSIM_PRO_ENDGAME_FAIL",
                sourceManifest = ManifestRelativePath,
                sourceArchiveSha256 = manifest == null || manifest.source == null ? "" :
                    manifest.source.archiveSha256,
                model = GoProductionModelLayout.ModelName,
                rules = "Tromp-Taylor positional-superko area komi-7.5",
                budgets = manifest == null ? new int[0] : manifest.budgets,
                positionCount = manifest == null ? 0 : manifest.positionCount,
                sampleCount = ExpectedSampleCount(),
                positionStart = ReadInt("positionStart"),
                positionLimit = ReadInt("positionLimit"),
                budgetStart = ReadInt("budgetStart"),
                budgetLimit = ReadInt("budgetLimit"),
                failure = failure ?? "",
                activeSearchPhase = ReadInt("activeSearchPhase"),
                activeSearchVisits = ReadInt("activeSearchVisits"),
                activeSearchTargetVisits = ReadInt("activeSearchTargetVisits"),
                activeSearchTreeNodes = ReadInt("activeSearchTreeNodes"),
                activeSearchTreeEdges = ReadInt("activeSearchTreeEdges"),
                activeSearchStepCalls = ReadInt("activeSearchStepCalls"),
                activeLegalMoveChecks = ReadInt("activeLegalMoveChecks"),
                activeSimulationPlayCalls = ReadInt("activeSimulationPlayCalls"),
                activeHistoryEntriesExamined = ReadInt("activeHistoryEntriesExamined"),
                activeHistoryQueryCount = ReadInt("activeHistoryQueryCount"),
                activeHistoryBucketHitCount = ReadInt("activeHistoryBucketHitCount"),
                activeHistoryBucketMissCount = ReadInt("activeHistoryBucketMissCount"),
                activeHistoryFullScanCount = ReadInt("activeHistoryFullScanCount"),
                activeHistoryMaxEntriesExamined = ReadInt("activeHistoryMaxEntriesExamined"),
                activeLadderSearchTransitions = ReadInt("activeLadderSearchTransitions"),
                activeLadderNodes = ReadInt("activeLadderNodes"),
                activeGameMoveCount = ReadInt("activeGameMoveCount"),
                activeGameRevision = ReadInt("activeGameRevision"),
                activeControllerAiMoves = ReadInt("activeControllerAiMoves"),
                activeBaselineMoveCount = ReadInt("activeBaselineMoveCount"),
                activeBaselineAiMoves = ReadInt("activeBaselineAiMoves"),
                activeControllerState = ReadInt("activeControllerState"),
                activeControllerAutoStart = ReadBool("activeControllerAutoStart"),
                activeSearchElapsedSeconds = ReadFloat("activeSearchElapsedSeconds"),
                rows = BuildRows()
            };
            File.WriteAllText(path, JsonUtility.ToJson(trace, true));
            return path;
        }

        private static TraceRow[] BuildRows()
        {
            List<TraceRow> rows = new List<TraceRow>();
            if (probeProgram == null || manifest == null)
                return rows.ToArray();
            int[] selected = ReadIntArray("selectedMoves");
            int[] sampledSelected = ReadIntArray("sampledSelectedMoves");
            int[] deterministicRoot = ReadIntArray("deterministicRootMoves");
            int[] deterministicVisits = ReadIntArray("deterministicRootVisits");
            int[] policy = ReadIntArray("policyMoves");
            int[] actual = ReadIntArray("actualVisits");
            int[] target = ReadIntArray("targetSearchVisits");
            int[] moveCounts = ReadIntArray("moveCounts");
            int[] terminalStates = ReadIntArray("terminalStates");
            bool[] aiResigned = ReadBoolArray("aiResigned");
            int[] treeNodes = ReadIntArray("treeNodes");
            int[] treeEdges = ReadIntArray("treeEdges");
            int[] rootVisits = ReadIntArray("selectedRootVisits");
            int[] stages = ReadIntArray("readerStages");
            int[] ownership = ReadIntArray("ownershipReadbacks");
            int[] revisions = ReadIntArray("rootOutputRevisions");
            int[] tokens = ReadIntArray("rootOutputSearchTokens");
            int[] starts = ReadIntArray("startFrames");
            int[] ends = ReadIntArray("endFrames");
            float[] elapsed = ReadFloatArray("elapsedSeconds");
            float[] values = ReadFloatArray("rootMeanValues");
            int[] searchFrames = ReadIntArray("searchFrames");
            int[] searchStepCalls = ReadIntArray("searchStepCalls");
            int[] legalMoveChecks = ReadIntArray("legalMoveChecks");
            int[] simulationPlayCalls = ReadIntArray("simulationPlayCalls");
            int[] historyEntriesExamined = ReadIntArray("historyEntriesExamined");
            int[] historyQueryCount = ReadIntArray("historyQueryCount");
            int[] historyBucketHitCount = ReadIntArray("historyBucketHitCount");
            int[] historyBucketMissCount = ReadIntArray("historyBucketMissCount");
            int[] historyFullScanCount = ReadIntArray("historyFullScanCount");
            int[] historyMaxEntriesExamined = ReadIntArray("historyMaxEntriesExamined");
            int[] ladderStepCalls = ReadIntArray("ladderStepCalls");
            int[] ladderSearchTransitions = ReadIntArray("ladderSearchTransitions");
            int[] ladderNodes = ReadIntArray("ladderNodes");
            int[] gpuPasses = ReadIntArray("gpuPasses");
            int[] gpuGraphFrames = ReadIntArray("gpuGraphFrames");
            int[] readbackRequests = ReadIntArray("readbackRequests");
            float[] searchMilliseconds = ReadFloatArray("searchMilliseconds");
            float[] featureMilliseconds = ReadFloatArray("featureMilliseconds");
            float[] ladderMilliseconds = ReadFloatArray("ladderMilliseconds");
            float[] scoreUtilityMilliseconds = ReadFloatArray("scoreUtilityMilliseconds");
            float[] gpuEvaluationMilliseconds = ReadFloatArray("gpuEvaluationMilliseconds");
            float[] gpuDispatchMilliseconds = ReadFloatArray("gpuDispatchMilliseconds");
            float[] readbackMilliseconds = ReadFloatArray("readbackMilliseconds");
            int[] candidateMoves = ReadIntArray("rootCandidateMoves");
            int[] candidateVisits = ReadIntArray("rootCandidateVisits");
            int[] decisionCounts = ReadIntArray("referenceDecisionCounts");
            int[] decisionSimulationIndex = ReadIntArray("referenceDecisionSimulationIndex");
            int[] decisionParentVisits = ReadIntArray("referenceDecisionParentVisits");
            int[] decisionSelectedMove = ReadIntArray("referenceDecisionSelectedMove");
            int[] decisionSelectedEdgeVisits = ReadIntArray("referenceDecisionSelectedEdgeVisits");
            float[] decisionPolicyMass = ReadFloatArray("referenceDecisionPolicyMassVisited");
            float[] decisionTotalWeight = ReadFloatArray("referenceDecisionTotalChildWeight");
            float[] decisionFpu = ReadFloatArray("referenceDecisionFpu");
            float[] decisionExploration = ReadFloatArray("referenceDecisionExploration");
            float[] decisionBestScore = ReadFloatArray("referenceDecisionBestScore");
            float[] decisionSecondBestScore = ReadFloatArray("referenceDecisionSecondBestScore");
            float[] decisionSelectedPrior = ReadFloatArray("referenceDecisionSelectedPrior");
            float[] decisionSelectedQ = ReadFloatArray("referenceDecisionSelectedQ");
            float[] decisionSelectedU = ReadFloatArray("referenceDecisionSelectedU");
            float[] decisionSelectedScore = ReadFloatArray("referenceDecisionSelectedScore");
            int[] decisionCandidateMoves = ReadIntArray("referenceDecisionCandidateMoves");
            int[] decisionCandidateVisits = ReadIntArray("referenceDecisionCandidateVisits");
            float[] decisionCandidatePrior = ReadFloatArray("referenceDecisionCandidatePrior");
            float[] decisionCandidateQ = ReadFloatArray("referenceDecisionCandidateQ");
            float[] decisionCandidateU = ReadFloatArray("referenceDecisionCandidateU");
            float[] decisionCandidateScore = ReadFloatArray("referenceDecisionCandidateScore");
            int[] rawVisitArgmaxVisits = ReadIntArray("rawRootVisitArgmaxVisits");
            int[] rawVisitArgmaxTieCounts = ReadIntArray("rawRootVisitArgmaxTieCounts");
            int[] rawVisitArgmaxMoves = ReadIntArray("rawRootVisitArgmaxMoves");
            int positionStart = ReadInt("positionStart");
            int positionLimit = ReadInt("positionLimit");
            int budgetStart = ReadInt("budgetStart");
            int budgetLimit = ReadInt("budgetLimit");
            int runPositionCount = positionLimit - positionStart;
            int sampleCount = runPositionCount * (budgetLimit - budgetStart);
            for (int i = 0; i < sampleCount; i++)
            {
                int positionIndex = positionStart + i % runPositionCount;
                int budgetIndex = budgetStart + i / runPositionCount;
                ManifestPosition position = manifest.positions[positionIndex];
                int[] sampleCandidateMoves = new int[
                    GoProfessionalEndgameProbe.ROOT_CANDIDATE_COUNT];
                int[] sampleCandidateVisits = new int[
                    GoProfessionalEndgameProbe.ROOT_CANDIDATE_COUNT];
                for (int candidate = 0;
                    candidate < GoProfessionalEndgameProbe.ROOT_CANDIDATE_COUNT;
                    candidate++)
                {
                    int sourceIndex = i * GoProfessionalEndgameProbe.ROOT_CANDIDATE_COUNT +
                        candidate;
                    sampleCandidateMoves[candidate] = candidateMoves == null ||
                        sourceIndex >= candidateMoves.Length ?
                        GoGame.NONE : candidateMoves[sourceIndex];
                    sampleCandidateVisits[candidate] = candidateVisits == null ||
                        sourceIndex >= candidateVisits.Length ?
                        0 : candidateVisits[sourceIndex];
                }
                TraceDecision[] decisionTrace = new TraceDecision[0];
                int decisionCount = decisionCounts == null || i >= decisionCounts.Length ?
                    0 : Mathf.Clamp(decisionCounts[i], 0,
                        GoProfessionalEndgameProbe.DECISION_TRACE_CAPACITY);
                if (decisionCount > 0 && decisionSimulationIndex != null &&
                    decisionSelectedMove != null && decisionCandidateMoves != null)
                {
                    decisionTrace = new TraceDecision[decisionCount];
                    int decisionBase = i * GoProfessionalEndgameProbe.DECISION_TRACE_CAPACITY;
                    int candidateWidth = GoMctsSearch.REFERENCE_DECISION_CANDIDATE_COUNT;
                    for (int decision = 0; decision < decisionCount; decision++)
                    {
                        int sourceIndex = decisionBase + decision;
                        TraceCandidate[] candidates = new TraceCandidate[candidateWidth];
                        int candidateBase = sourceIndex * candidateWidth;
                        for (int candidate = 0; candidate < candidateWidth; candidate++)
                        {
                            int candidateIndex = candidateBase + candidate;
                            int move = candidateIndex < decisionCandidateMoves.Length ?
                                decisionCandidateMoves[candidateIndex] : GoGame.NONE;
                            candidates[candidate] = new TraceCandidate
                            {
                                move = move,
                                coordinate = Coordinate(move),
                                visits = decisionCandidateVisits != null &&
                                    candidateIndex < decisionCandidateVisits.Length ?
                                    decisionCandidateVisits[candidateIndex] : 0,
                                prior = decisionCandidatePrior != null &&
                                    candidateIndex < decisionCandidatePrior.Length ?
                                    decisionCandidatePrior[candidateIndex] : 0f,
                                q = decisionCandidateQ != null &&
                                    candidateIndex < decisionCandidateQ.Length ?
                                    decisionCandidateQ[candidateIndex] : 0f,
                                u = decisionCandidateU != null &&
                                    candidateIndex < decisionCandidateU.Length ?
                                    decisionCandidateU[candidateIndex] : 0f,
                                score = decisionCandidateScore != null &&
                                    candidateIndex < decisionCandidateScore.Length ?
                                    decisionCandidateScore[candidateIndex] : 0f
                            };
                        }
                        int selectedDecisionMove = decisionSelectedMove[sourceIndex];
                        decisionTrace[decision] = new TraceDecision
                        {
                            simulationIndex = decisionSimulationIndex[sourceIndex],
                            parentVisits = decisionParentVisits != null &&
                                sourceIndex < decisionParentVisits.Length ?
                                decisionParentVisits[sourceIndex] : 0,
                            selectedMove = selectedDecisionMove,
                            selectedCoordinate = Coordinate(selectedDecisionMove),
                            selectedEdgeVisits = decisionSelectedEdgeVisits != null &&
                                sourceIndex < decisionSelectedEdgeVisits.Length ?
                                decisionSelectedEdgeVisits[sourceIndex] : 0,
                            policyMassVisited = decisionPolicyMass != null &&
                                sourceIndex < decisionPolicyMass.Length ?
                                decisionPolicyMass[sourceIndex] : 0f,
                            totalChildWeight = decisionTotalWeight != null &&
                                sourceIndex < decisionTotalWeight.Length ?
                                decisionTotalWeight[sourceIndex] : 0f,
                            fpu = decisionFpu != null && sourceIndex < decisionFpu.Length ?
                                decisionFpu[sourceIndex] : 0f,
                            exploration = decisionExploration != null &&
                                sourceIndex < decisionExploration.Length ?
                                decisionExploration[sourceIndex] : 0f,
                            bestScore = decisionBestScore != null &&
                                sourceIndex < decisionBestScore.Length ?
                                decisionBestScore[sourceIndex] : 0f,
                            secondBestScore = decisionSecondBestScore != null &&
                                sourceIndex < decisionSecondBestScore.Length ?
                                decisionSecondBestScore[sourceIndex] : 0f,
                            selectedPrior = decisionSelectedPrior != null &&
                                sourceIndex < decisionSelectedPrior.Length ?
                                decisionSelectedPrior[sourceIndex] : 0f,
                            selectedQ = decisionSelectedQ != null &&
                                sourceIndex < decisionSelectedQ.Length ?
                                decisionSelectedQ[sourceIndex] : 0f,
                            selectedU = decisionSelectedU != null &&
                                sourceIndex < decisionSelectedU.Length ?
                                decisionSelectedU[sourceIndex] : 0f,
                            selectedScore = decisionSelectedScore != null &&
                                sourceIndex < decisionSelectedScore.Length ?
                                decisionSelectedScore[sourceIndex] : 0f,
                            candidates = candidates
                        };
                    }
                }
                int sampledMove = sampledSelected == null || i >= sampledSelected.Length ?
                    (selected == null ? GoGame.NONE : selected[i]) : sampledSelected[i];
                int deterministicMove = deterministicRoot == null ||
                    i >= deterministicRoot.Length ? GoGame.NONE : deterministicRoot[i];
                rows.Add(new TraceRow
                {
                    sampleIndex = i,
                    positionIndex = positionIndex,
                    budgetIndex = budgetIndex,
                    id = position.id,
                    sourceUrl = position.sourceUrl,
                    moveHashSha256 = position.moveHashSha256,
                    prefixMoveCount = position.prefixMoveCount,
                    sideToMove = position.sideToMove,
                    targetVisits = target == null ? 0 : target[i],
                    actualVisits = actual == null ? 0 : actual[i],
                    selectedMove = sampledMove,
                    selectedCoordinate = Coordinate(sampledMove),
                    sampledSelectedMove = sampledMove,
                    sampledSelectedCoordinate = Coordinate(sampledMove),
                    deterministicRootMove = deterministicMove,
                    deterministicRootCoordinate = Coordinate(deterministicMove),
                    deterministicRootVisits = deterministicVisits == null ||
                        i >= deterministicVisits.Length ? 0 : deterministicVisits[i],
                    policyMove = policy == null ? GoGame.NONE : policy[i],
                    policyCoordinate = Coordinate(policy == null ? GoGame.NONE : policy[i]),
                    moveCountAfterSearch = moveCounts == null ? 0 : moveCounts[i],
                    terminalState = ValueAt(terminalStates, i),
                    aiResigned = ValueAt(aiResigned, i),
                    treeNodes = treeNodes == null ? 0 : treeNodes[i],
                    treeEdges = treeEdges == null ? 0 : treeEdges[i],
                    selectedRootVisits = rootVisits == null ? 0 : rootVisits[i],
                    readerStages = stages == null ? 0 : stages[i],
                    ownershipReadbacks = ownership == null ? 0 : ownership[i],
                    rootOutputRevision = revisions == null ? 0 : revisions[i],
                    rootOutputSearchToken = tokens == null ? 0 : tokens[i],
                    startFrame = starts == null ? 0 : starts[i],
                    endFrame = ends == null ? 0 : ends[i],
                    elapsedSeconds = elapsed == null ? 0f : elapsed[i],
                    rootMeanValue = values == null ? 0f : values[i],
                    searchFrames = ValueAt(searchFrames, i),
                    searchStepCalls = ValueAt(searchStepCalls, i),
                    legalMoveChecks = ValueAt(legalMoveChecks, i),
                    simulationPlayCalls = ValueAt(simulationPlayCalls, i),
                    historyEntriesExamined = ValueAt(historyEntriesExamined, i),
                    historyQueryCount = ValueAt(historyQueryCount, i),
                    historyBucketHitCount = ValueAt(historyBucketHitCount, i),
                    historyBucketMissCount = ValueAt(historyBucketMissCount, i),
                    historyFullScanCount = ValueAt(historyFullScanCount, i),
                    historyMaxEntriesExamined = ValueAt(historyMaxEntriesExamined, i),
                    ladderStepCalls = ValueAt(ladderStepCalls, i),
                    ladderSearchTransitions = ValueAt(ladderSearchTransitions, i),
                    ladderNodes = ValueAt(ladderNodes, i),
                    gpuPasses = ValueAt(gpuPasses, i),
                    gpuGraphFrames = ValueAt(gpuGraphFrames, i),
                    readbackRequests = ValueAt(readbackRequests, i),
                    searchMilliseconds = ValueAt(searchMilliseconds, i),
                    featureMilliseconds = ValueAt(featureMilliseconds, i),
                    ladderMilliseconds = ValueAt(ladderMilliseconds, i),
                    scoreUtilityMilliseconds = ValueAt(scoreUtilityMilliseconds, i),
                    gpuEvaluationMilliseconds = ValueAt(gpuEvaluationMilliseconds, i),
                    gpuDispatchMilliseconds = ValueAt(gpuDispatchMilliseconds, i),
                    readbackMilliseconds = ValueAt(readbackMilliseconds, i),
                    rootCandidateMoves = sampleCandidateMoves,
                    rootCandidateVisits = sampleCandidateVisits,
                    rootDecisionTrace = decisionTrace,
                    rawRootVisitArgmaxVisits = rawVisitArgmaxVisits == null ||
                        i >= rawVisitArgmaxVisits.Length ? 0 : rawVisitArgmaxVisits[i],
                    rawRootVisitArgmaxTieCount = rawVisitArgmaxTieCounts == null ||
                        i >= rawVisitArgmaxTieCounts.Length ? 0 : rawVisitArgmaxTieCounts[i],
                    rawRootVisitArgmaxMoves = BuildRawArgmaxMoves(i,
                        rawVisitArgmaxTieCounts, rawVisitArgmaxMoves)
                });
            }
            return rows.ToArray();
        }

        private static int ExpectedSampleCount()
        {
            int positions = ReadInt("positionLimit") - ReadInt("positionStart");
            int budgets = ReadInt("budgetLimit") - ReadInt("budgetStart");
            return Mathf.Max(0, positions * budgets);
        }

        private static Manifest LoadManifest()
        {
            string path = Path.Combine(Application.dataPath, ManifestRelativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Professional endgame manifest is missing", path);
            Manifest value = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (value == null)
                throw new InvalidOperationException("Professional endgame manifest is invalid");
            return value;
        }

        private static void ValidateManifest(Manifest value)
        {
            if (value.schema != "pure-udon-go.professional-endgame-corpus.v1" ||
                value.positionCount != GoProfessionalEndgameProbe.POSITION_COUNT ||
                value.positions == null || value.positions.Length != value.positionCount ||
                value.budgets == null || value.budgets.Length != GoProfessionalEndgameProbe.BUDGET_COUNT)
                throw new InvalidOperationException("Professional endgame manifest schema/count mismatch");
            if (value.budgets[0] != GoProfessionalEndgameProbe.BENCHMARK_BEGINNER_VISITS ||
                value.budgets[1] != GoProfessionalEndgameProbe.BENCHMARK_ADVANCED_VISITS)
                throw new InvalidOperationException("Professional endgame budgets drifted from the fixed benchmark budget contract");
            for (int i = 0; i < value.positions.Length; i++)
            {
                if (string.IsNullOrEmpty(value.positions[i].id) ||
                    value.positions[i].moves == null || value.positions[i].moves.Length < 1)
                    throw new InvalidOperationException("Professional endgame position is incomplete: " + i);
            }
        }

        private static string Coordinate(int location)
        {
            if (location == GoGame.PASS) return "pass";
            if (location < 0 || location >= GoGame.AREA) return "none";
            const string columns = "ABCDEFGHJKLMNOPQRST";
            return columns[location % 19].ToString() +
                (19 - location / 19).ToString();
        }

        private static int[] BuildRawArgmaxMoves(int sampleIndex,
            int[] tieCounts, int[] moves)
        {
            if (tieCounts == null || moves == null || sampleIndex < 0 ||
                sampleIndex >= tieCounts.Length)
                return new int[0];
            int count = Mathf.Clamp(tieCounts[sampleIndex], 0,
                GoProfessionalEndgameProbe.RAW_ROOT_TIE_CAPACITY);
            int[] result = new int[count];
            int baseIndex = sampleIndex * GoProfessionalEndgameProbe.RAW_ROOT_TIE_CAPACITY;
            for (int i = 0; i < count; i++)
                result[i] = baseIndex + i < moves.Length ? moves[baseIndex + i] : GoGame.NONE;
            return result;
        }

        private static int[] ReadIntArray(string name)
        { return probeProgram.GetProgramVariable(name) as int[]; }
        private static int ReadInt(string name)
        {
            object value = probeProgram.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
        }
        private static float[] ReadFloatArray(string name)
        { return probeProgram.GetProgramVariable(name) as float[]; }
        private static bool[] ReadBoolArray(string name)
        { return probeProgram.GetProgramVariable(name) as bool[]; }
        private static int ValueAt(int[] values, int index)
        { return values == null || index < 0 || index >= values.Length ? 0 : values[index]; }
        private static float ValueAt(float[] values, int index)
        { return values == null || index < 0 || index >= values.Length ? 0f : values[index]; }
        private static bool ValueAt(bool[] values, int index)
        { return values != null && index >= 0 && index < values.Length && values[index]; }
        private static float ReadFloat(string name)
        {
            object value = probeProgram.GetProgramVariable(name);
            return value == null ? 0f : Convert.ToSingle(value);
        }
        private static bool ReadBool(string name)
        {
            object value = probeProgram.GetProgramVariable(name);
            return value != null && Convert.ToBoolean(value);
        }
        private static string ReadString(string name)
        {
            object value = probeProgram.GetProgramVariable(name);
            return value == null ? "" : value.ToString();
        }
        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup >= deadline)
            {
                if (probeProgram != null)
                    message += " phase=" + ReadInt("phase") +
                        " position=" + ReadInt("positionIndex") +
                        " budget=" + ReadInt("budgetIndex") +
                        " moveCursor=" + ReadInt("moveCursor") +
                        " sample=" + ReadInt("sampleIndex") +
                        " activePhase=" + ReadInt("activeSearchPhase") +
                        " activeVisits=" + ReadInt("activeSearchVisits") + "/" +
                        ReadInt("activeSearchTargetVisits") +
                        " activeNodes=" + ReadInt("activeSearchTreeNodes") +
                        " activeEdges=" + ReadInt("activeSearchTreeEdges") +
                        " activeSteps=" + ReadInt("activeSearchStepCalls") +
                        " activeLegal=" + ReadInt("activeLegalMoveChecks") +
                        " activePlays=" + ReadInt("activeSimulationPlayCalls") +
                        " activeHistory=" + ReadInt("activeHistoryEntriesExamined") +
                        " activeQueries=" + ReadInt("activeHistoryQueryCount") +
                        " activeBucketHits=" + ReadInt("activeHistoryBucketHitCount") +
                        " activeBucketMisses=" + ReadInt("activeHistoryBucketMissCount") +
                        " activeFullScans=" + ReadInt("activeHistoryFullScanCount") +
                        " activeMaxEntries=" + ReadInt("activeHistoryMaxEntriesExamined") +
                        " activeLadderTransitions=" + ReadInt("activeLadderSearchTransitions") +
                        " activeLadderNodes=" + ReadInt("activeLadderNodes") +
                        " activeGameMoves=" + ReadInt("activeGameMoveCount") +
                        " activeGameRevision=" + ReadInt("activeGameRevision") +
                        " activeAiMoves=" + ReadInt("activeControllerAiMoves") +
                        " baselineMoves=" + ReadInt("activeBaselineMoveCount") +
                        " baselineAiMoves=" + ReadInt("activeBaselineAiMoves") +
                        " activeControllerState=" + ReadInt("activeControllerState") +
                        " activeAutoStart=" + ReadBool("activeControllerAutoStart") +
                        " activeElapsed=" + ReadFloat("activeSearchElapsedSeconds");
                Fail(message);
            }
        }
        private static void Fail(string message)
        {
            if (resultWritten) return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_PRO_ENDGAME_FAIL " + message);
            // Preserve completed rows on a hard timeout or other real failure.
            // This is diagnostic evidence only; the trace remains FAIL and is
            // never treated as a complete corpus result by the comparator.
            if (probeProgram != null && manifest != null)
                WriteTrace(false, message);
            StopWithResult(false);
        }
        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PRO_ENDGAME_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public int positionCount;
            public int[] budgets;
            public ManifestSource source;
            public ManifestPosition[] positions;
        }
        [Serializable]
        private sealed class ManifestSource
        { public string archiveSha256; }
        [Serializable]
        private sealed class ManifestPosition
        {
            public string id;
            public string sourceUrl;
            public string moveHashSha256;
            public int prefixMoveCount;
            public string sideToMove;
            public int[] moves;
        }
        [Serializable]
            private sealed class Trace
        {
            public string schema;
            public string status;
            public string sourceManifest;
            public string sourceArchiveSha256;
            public string model;
            public string rules;
            public int[] budgets;
            public int positionCount;
            public int sampleCount;
            public int positionStart;
            public int positionLimit;
            public int budgetStart;
            public int budgetLimit;
            public string failure;
            public int activeSearchPhase;
            public int activeSearchVisits;
            public int activeSearchTargetVisits;
            public int activeSearchTreeNodes;
            public int activeSearchTreeEdges;
            public int activeSearchStepCalls;
            public int activeLegalMoveChecks;
            public int activeSimulationPlayCalls;
            public int activeHistoryEntriesExamined;
            public int activeHistoryQueryCount;
            public int activeHistoryBucketHitCount;
            public int activeHistoryBucketMissCount;
            public int activeHistoryFullScanCount;
            public int activeHistoryMaxEntriesExamined;
            public int activeLadderSearchTransitions;
            public int activeLadderNodes;
            public int activeGameMoveCount;
            public int activeGameRevision;
            public int activeControllerAiMoves;
            public int activeBaselineMoveCount;
            public int activeBaselineAiMoves;
            public int activeControllerState;
            public bool activeControllerAutoStart;
            public float activeSearchElapsedSeconds;
            public TraceRow[] rows;
        }
        [Serializable]
        private sealed class TraceRow
        {
            public int sampleIndex;
            public int positionIndex;
            public int budgetIndex;
            public string id;
            public string sourceUrl;
            public string moveHashSha256;
            public int prefixMoveCount;
            public string sideToMove;
            public int targetVisits;
            public int actualVisits;
            public int sampledSelectedMove;
            public string sampledSelectedCoordinate;
            public int deterministicRootMove;
            public string deterministicRootCoordinate;
            public int deterministicRootVisits;
            // Legacy aliases retained so existing reports/tools can still read
            // selectedMove/selectedCoordinate as the sampled product move.
            public int selectedMove;
            public string selectedCoordinate;
            public int policyMove;
            public string policyCoordinate;
            public int moveCountAfterSearch;
            public int terminalState;
            public bool aiResigned;
            public int treeNodes;
            public int treeEdges;
            public int selectedRootVisits;
            public int readerStages;
            public int ownershipReadbacks;
            public int rootOutputRevision;
            public int rootOutputSearchToken;
            public int startFrame;
            public int endFrame;
            public float elapsedSeconds;
            public float rootMeanValue;
            public int searchFrames;
            public int searchStepCalls;
            public int legalMoveChecks;
            public int simulationPlayCalls;
            public int historyEntriesExamined;
            public int historyQueryCount;
            public int historyBucketHitCount;
            public int historyBucketMissCount;
            public int historyFullScanCount;
            public int historyMaxEntriesExamined;
            public int ladderStepCalls;
            public int ladderSearchTransitions;
            public int ladderNodes;
            public int gpuPasses;
            public int gpuGraphFrames;
            public int readbackRequests;
            public float searchMilliseconds;
            public float featureMilliseconds;
            public float ladderMilliseconds;
            public float scoreUtilityMilliseconds;
            public float gpuEvaluationMilliseconds;
            public float gpuDispatchMilliseconds;
            public float readbackMilliseconds;
            public int[] rootCandidateMoves;
            public int[] rootCandidateVisits;
            public int rawRootVisitArgmaxVisits;
            public int rawRootVisitArgmaxTieCount;
            public int[] rawRootVisitArgmaxMoves;
            public TraceDecision[] rootDecisionTrace;
        }

        [Serializable]
        private sealed class TraceDecision
        {
            public int simulationIndex;
            public int parentVisits;
            public int selectedMove;
            public string selectedCoordinate;
            public int selectedEdgeVisits;
            public float policyMassVisited;
            public float totalChildWeight;
            public float fpu;
            public float exploration;
            public float bestScore;
            public float secondBestScore;
            public float selectedPrior;
            public float selectedQ;
            public float selectedU;
            public float selectedScore;
            public TraceCandidate[] candidates;
        }

        [Serializable]
        private sealed class TraceCandidate
        {
            public int move;
            public string coordinate;
            public int visits;
            public float prior;
            public float q;
            public float u;
            public float score;
        }

        [Serializable]
        private sealed class ReferenceInput
        {
            public string schema;
            public int targetVisits;
            public float explorationConstant;
            public int searchSemanticsMode;
            public bool positionalSuperko;
            public bool areaScoring;
            public float rootMoveTemperature;
            public int rootPolicyTopK;
            public int rootSelectionSeed;
            public ReferenceExpansion[] expansions;
            public int expectedRootVisits;
            public int expectedDeterministicRootMove;
            public ReferenceEdge[] expectedRootEdges;
        }

        [Serializable]
        private sealed class ReferenceExpansion
        {
            public int nodeId;
            public int parentNodeId;
            public int parentMove;
            public int[] legalMoves;
            public float[] policySpatialLogits;
            public float policyPassLogit;
            public float valueLogit0;
            public float valueLogit1;
            public float valueLogit2;
            public float scoreMean;
            public float scoreStdev;
        }

        [Serializable]
        private sealed class ReferenceEdge
        {
            public int move;
            public float prior;
            public int visits;
            public float valueSum;
        }
    }
}
#endif
