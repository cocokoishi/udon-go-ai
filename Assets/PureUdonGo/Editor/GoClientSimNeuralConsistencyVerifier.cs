#if UNITY_EDITOR
using System;
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
    /// Exports fixed neural outputs produced by serialized Udon in ClientSim.
    /// The Udon probe constructs each position, waits for the real
    /// frame-scheduled encoder/GPU/readback/PUCT path, and exports the first
    /// root tensors. This verifier proves output completeness and transport
    /// identity only; it does not perform a numeric comparison with KataGo.
    /// Numeric comparisons must be run by a separate, explicitly named oracle
    /// tool and must consume this export as an input.
    /// </summary>
    public static class GoClientSimNeuralConsistencyVerifier
    {
        private const double TimeoutSeconds = 900.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimNeuralOutputExportVerifier.Pending";
        private const string FirstScenarioSessionKey =
            "PureUdonGo.ClientSimNeuralOutputExportVerifier.FirstScenario";
        private const string ScenarioCountSessionKey =
            "PureUdonGo.ClientSimNeuralOutputExportVerifier.ScenarioCount";
        private const string ExportLabelSessionKey =
            "PureUdonGo.ClientSimNeuralOutputExportVerifier.ExportLabel";
        private const string TimeoutSessionKey =
            "PureUdonGo.ClientSimNeuralOutputExportVerifier.TimeoutSeconds";
        private const string OutputFolderName = "GoNeuralOutputExport";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static int exportFirstScenario;
        private static int exportCaseCount;
        private static string exportLabel;
        private static double activeTimeoutSeconds = TimeoutSeconds;
        private static UdonBehaviour probeProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour readerProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour encoderProgram;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false)) return;
            exportFirstScenario = SessionState.GetInt(FirstScenarioSessionKey, 0);
            exportCaseCount = SessionState.GetInt(ScenarioCountSessionKey, 4);
            exportLabel = SessionState.GetString(ExportLabelSessionKey, "fixed");
            activeTimeoutSeconds = SessionState.GetInt(TimeoutSessionKey, (int)TimeoutSeconds);
            deadline = EditorApplication.timeSinceStartup + activeTimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Neural Outputs")]
        public static void ExportClientSimNeuralOutputs()
        {
            BeginExport(0, 4, "fixed");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Random Neural Outputs")]
        public static void ExportClientSimRandomNeuralOutputs()
        {
            BeginExport(4, 4, "random");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Random Neural Case 0")]
        public static void ExportClientSimRandomNeuralCase()
        {
            BeginExport(4, 1, "random-case-a");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Random Neural Case 1")]
        public static void ExportClientSimRandomNeuralCase1()
        {
            BeginExport(5, 1, "random-case-b");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Random Neural Case 2")]
        public static void ExportClientSimRandomNeuralCase2()
        {
            BeginExport(6, 1, "random-case-c");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Random Neural Case 3")]
        public static void ExportClientSimRandomNeuralCase3()
        {
            BeginExport(7, 1, "random-case-d");
        }

        private static void BeginExport(int firstScenario, int caseCount, string label)
        {
            ResetRunner();
            exportFirstScenario = firstScenario;
            exportCaseCount = caseCount;
            exportLabel = label;
            activeTimeoutSeconds = caseCount == 1 ? 90.0 : TimeoutSeconds;
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " +
                    Editor.GoWorldGenerator.ScenePath);

            GoGeneratedWorld root = UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoGame game = pool != null && pool.tableRoots != null &&
                pool.tableRoots.Length > 0 && pool.tableRoots[0] != null
                ? pool.tableRoots[0].GetComponentInChildren<GoGame>(true)
                : UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller = game == null ? null :
                game.GetComponentInChildren<GoAiController>(true);
            GoFeatureEncoder encoder = controller == null ? null : controller.encoder;
            if (root == null || game == null || controller == null ||
                encoder == null || game.blackDifficulty == null || game.whiteDifficulty == null)
                throw new InvalidOperationException(
                    "Generated Go neural runtime objects or profiles were not found");
            aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
            readerProgram = controller.reader == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.reader);
            searchProgram = controller.search == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.search);
            encoderProgram = controller.encoder == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.encoder);

            string probeAssetPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoNeuralConsistencyProbe), "Neural-consistency");
            GameObject probeObject = new GameObject("ClientSim Neural Consistency Probe");
            probeObject.transform.SetParent(root.transform, false);
            GoNeuralConsistencyProbe probe =
                probeObject.AddUdonSharpComponent<GoNeuralConsistencyProbe>();
            probe.game = game;
            probe.controller = controller;
            probe.encoder = encoder;
            probe.blackDifficulty = game.blackDifficulty;
            probe.whiteDifficulty = game.whiteDifficulty;
            probe.aiSettings = game.aiSettings;
            probe.firstScenario = exportFirstScenario;
            probe.scenarioCount = exportCaseCount;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe, probeAssetPath);

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
            SessionState.SetInt(FirstScenarioSessionKey, exportFirstScenario);
            SessionState.SetInt(ScenarioCountSessionKey, exportCaseCount);
            SessionState.SetInt(TimeoutSessionKey, (int)activeTimeoutSeconds);
            SessionState.SetString(ExportLabelSessionKey, exportLabel);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + activeTimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " cases=" + exportCaseCount +
                " firstScenario=" + exportFirstScenario +
                " label=" + exportLabel + " visits=" + GoNeuralConsistencyProbe.DIAGNOSTIC_VISITS + " outputFolder=" + OutputFolderName +
                " timeoutSeconds=" + activeTimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        // Preserve the old local executeMethod entry point while routing it
        // through the accurately named output-export operation.
        public static void VerifyClientSimNeuralConsistency()
        {
            ExportClientSimNeuralOutputs();
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            probeSent = false;
            probeProgram = null;
            aiProgram = null;
            readerProgram = null;
            searchProgram = null;
            encoderProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before neural consistency probe completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying) return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_PLAYMODE_ENTERED");
            }
            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                if (probeProgram == null)
                {
                    GoNeuralConsistencyProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoNeuralConsistencyProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon neural consistency probe was not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_EVENT name=RunNeuralConsistencyProbe");
                    probeProgram.SendCustomEvent("RunNeuralConsistencyProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool(probeProgram, "probeFinished"))
                {
                    FailIfTimedOut("Udon neural consistency probe did not finish");
                    return;
                }

                bool passed = ReadBool(probeProgram, "probePassed");
                int[] visits = ReadIntArray(probeProgram, "actualVisits");
                int[] selected = ReadIntArray(probeProgram, "selectedMoves");
                int[] revisions = ReadIntArray(probeProgram, "rootOutputRevisions");
                int[] tokens = ReadIntArray(probeProgram, "rootOutputSearchTokens");
                int[] stages = ReadIntArray(probeProgram, "readerStages");
                int[] ownership = ReadIntArray(probeProgram, "ownershipReadbacks");
                int[] moveCounts = ReadIntArray(probeProgram, "scenarioMoveCounts");
                float[] policy = ReadFloatArray(probeProgram, "rootPolicySpatial");
                float[] policyPass = ReadFloatArray(probeProgram, "rootPolicyPass");
                float[] value = ReadFloatArray(probeProgram, "rootValue");
                float[] score = ReadFloatArray(probeProgram, "rootScore");
                float[] ownershipLogits = ReadFloatArray(probeProgram, "rootOwnership");
                float[] inputSpatial = ReadFloatArray(probeProgram, "rootInputSpatial");
                float[] inputGlobal = ReadFloatArray(probeProgram, "rootInputGlobal");
                string failure = ReadString(probeProgram, "failure");
                string outputPath = WriteExport(passed, visits, selected, revisions, tokens,
                    stages, ownership, moveCounts, policy, policyPass, value, score,
                    ownershipLogits, inputSpatial, inputGlobal, failure);
                Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_RESULT passed=" + passed +
                    " cases=" + exportCaseCount + " firstScenario=" + exportFirstScenario +
                    " export=" + outputPath + " failure=" + failure);
                if (passed && HasExpectedArrays(visits, revisions, tokens, stages,
                    ownership, policy, policyPass, value, score, ownershipLogits,
                    inputSpatial, inputGlobal))
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_OUTPUT_EXPORT_PASS cases=" +
                        exportCaseCount +
                        " visits=" + GoNeuralConsistencyProbe.DIAGNOSTIC_VISITS + " rootStages=1 numericComparison=not-performed");
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon neural consistency probe failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim neural consistency inspection exception: " + exception);
            }
        }

        private static bool HasExpectedArrays(int[] visits, int[] revisions, int[] tokens,
            int[] stages, int[] ownership, float[] policy, float[] policyPass,
            float[] value, float[] score, float[] ownershipLogits,
            float[] inputSpatial, float[] inputGlobal)
        {
            int cases = GoNeuralConsistencyProbe.CASE_COUNT;
            return visits != null && visits.Length >= cases && revisions != null &&
                revisions.Length >= cases && tokens != null && tokens.Length >= cases &&
                stages != null && stages.Length >= cases && ownership != null &&
                ownership.Length >= cases && policy != null &&
                policy.Length >= cases * GoGame.AREA && policyPass != null &&
                policyPass.Length >= cases && value != null && value.Length >= cases * 3 &&
                score != null && score.Length >= cases * 4 && ownershipLogits != null &&
                ownershipLogits.Length >= cases * GoGame.AREA && inputSpatial != null &&
                inputSpatial.Length >= cases * GoFeatureEncoder.SPATIAL_COUNT &&
                inputGlobal != null && inputGlobal.Length >= cases * GoFeatureEncoder.GLOBAL_CHANNELS;
        }

        private static string WriteExport(bool passed, int[] visits, int[] selected,
            int[] revisions, int[] tokens, int[] stages, int[] ownership,
            int[] moveCounts, float[] policy, float[] policyPass, float[] value,
            float[] score, float[] ownershipLogits, float[] inputSpatial,
            float[] inputGlobal, string failure)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            // Unity clears the project Temp root during shutdown. Keep the
            // export in this checkout's evidence directory so it survives a
            // ClientSim process and is usable by a separately named numeric
            // oracle comparator. The same relative path exists in the
            // isolated verification project and in the production checkout.
            string outputFolder = Path.Combine(projectRoot, "Assets", "udon-go-ai",
                "Benchmarks", "KaTrain", "ClientSimExports", OutputFolderName);
            Directory.CreateDirectory(outputFolder);
            string fileName = exportLabel == "fixed" ? "udon-neural-output-export.json" :
                "udon-neural-output-export-" + exportLabel + ".json";
            string path = Path.Combine(outputFolder, fileName);
            Export export = new Export
            {
                schema = "pure-udon-go.clientsim-neural-output-export.v2",
                status = passed ? "CLIENTSIM_OUTPUT_EXPORT_PASS" : "CLIENTSIM_OUTPUT_EXPORT_FAIL",
                numericComparison = "NOT_PERFORMED_BY_THIS_VERIFIER",
                model = GoProductionModelLayout.ModelName,
                rules = "Tromp-Taylor positional-superko area komi-7.5",
                scenarioStart = exportFirstScenario,
                scenarioCount = exportCaseCount,
                cases = GetCaseNames(exportFirstScenario, exportCaseCount),
                visits = Slice(visits, exportFirstScenario, exportCaseCount),
                selectedMoves = Slice(selected, exportFirstScenario, exportCaseCount),
                rootOutputRevisions = Slice(revisions, exportFirstScenario, exportCaseCount),
                rootOutputSearchTokens = Slice(tokens, exportFirstScenario, exportCaseCount),
                readerStages = Slice(stages, exportFirstScenario, exportCaseCount),
                ownershipReadbacks = Slice(ownership, exportFirstScenario, exportCaseCount),
                scenarioMoveCounts = Slice(moveCounts, exportFirstScenario, exportCaseCount),
                rootPolicySpatial = Slice(policy, exportFirstScenario * GoGame.AREA,
                    exportCaseCount * GoGame.AREA),
                rootPolicyPass = Slice(policyPass, exportFirstScenario, exportCaseCount),
                rootValue = Slice(value, exportFirstScenario * 3, exportCaseCount * 3),
                rootScore = Slice(score, exportFirstScenario * 4, exportCaseCount * 4),
                rootOwnership = Slice(ownershipLogits, exportFirstScenario * GoGame.AREA,
                    exportCaseCount * GoGame.AREA),
                rootInputSpatial = Slice(inputSpatial,
                    exportFirstScenario * GoFeatureEncoder.SPATIAL_COUNT,
                    exportCaseCount * GoFeatureEncoder.SPATIAL_COUNT),
                rootInputGlobal = Slice(inputGlobal,
                    exportFirstScenario * GoFeatureEncoder.GLOBAL_CHANNELS,
                    exportCaseCount * GoFeatureEncoder.GLOBAL_CHANNELS),
                failure = failure ?? ""
            };
            File.WriteAllText(path, JsonUtility.ToJson(export, false));
            return path;
        }

        [Serializable]
        private sealed class Export
        {
            public string schema;
            public string status;
            public string numericComparison;
            public string model;
            public string rules;
            public int scenarioStart;
            public int scenarioCount;
            public string[] cases;
            public int[] visits;
            public int[] selectedMoves;
            public int[] rootOutputRevisions;
            public int[] rootOutputSearchTokens;
            public int[] readerStages;
            public int[] ownershipReadbacks;
            public int[] scenarioMoveCounts;
            public float[] rootPolicySpatial;
            public float[] rootPolicyPass;
            public float[] rootValue;
            public float[] rootScore;
            public float[] rootOwnership;
            public float[] rootInputSpatial;
            public float[] rootInputGlobal;
            public string failure;
        }

        private static string[] GetCaseNames(int first, int count)
        {
            string[] all = { "opening", "capture", "fight", "mixed", "random-a", "random-b", "random-c", "random-d" };
            string[] selected = new string[count];
            for (int i = 0; i < count; i++)
                selected[i] = all[first + i];
            return selected;
        }

        private static int[] Slice(int[] source, int start, int count)
        {
            if (source == null || start < 0 || count < 0 || start + count > source.Length)
                return null;
            int[] selected = new int[count];
            Array.Copy(source, start, selected, 0, count);
            return selected;
        }

        private static float[] Slice(float[] source, int start, int count)
        {
            if (source == null || start < 0 || count < 0 || start + count > source.Length)
                return null;
            float[] selected = new float[count];
            Array.Copy(source, start, selected, 0, count);
            return selected;
        }

        private static int[] ReadIntArray(UdonBehaviour program, string name)
        {
            return program.GetProgramVariable(name) as int[];
        }

        private static float[] ReadFloatArray(UdonBehaviour program, string name)
        {
            return program.GetProgramVariable(name) as float[];
        }

        private static int ReadInt(UdonBehaviour program, string name)
        {
            object value = program.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static bool ReadBool(UdonBehaviour program, string name)
        {
            object value = program.GetProgramVariable(name);
            return value != null && Convert.ToBoolean(value);
        }

        private static string ReadString(UdonBehaviour program, string name)
        {
            object value = program.GetProgramVariable(name);
            return value == null ? "" : value.ToString();
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup < deadline)
                return;
            ResolveDiagnosticPrograms();
            string progress = probeProgram == null ? "" :
                " scenario=" + ReadInt(probeProgram, "scenarioIndex") +
                " running=" + ReadBool(probeProgram, "diagnosticRunningScenario") +
                " encoding=" + ReadBool(probeProgram, "diagnosticEncodingRoot") +
                " encodeState=" + ReadInt(probeProgram, "diagnosticEncodeState") +
                " encodeStage=" + ReadInt(probeProgram, "diagnosticEncodeStage") +
                " controllerState=" + ReadInt(probeProgram, "diagnosticControllerState") +
                " searchPhase=" + ReadInt(probeProgram, "diagnosticSearchPhase") +
                " gameMoves=" + ReadInt(probeProgram, "diagnosticGameMoveCount") +
                " aiMoves=" + ReadInt(probeProgram, "diagnosticAiMoves") +
                " rootRevision=" + ReadInt(probeProgram, "diagnosticRootRevision");
            if (readerProgram != null)
                progress += " readerState=" + ReadInt(readerProgram, "readbackState") +
                    " readerStage=" + ReadInt(readerProgram, "readbackStage") +
                    " completedStages=" + ReadInt(readerProgram, "completedStages") +
                    " discardedCallbacks=" + ReadInt(readerProgram, "discardedCallbacks") +
                    " readerError=" + ReadString(readerProgram, "lastError");
            if (searchProgram != null)
                progress += " visits=" + ReadInt(searchProgram, "visitsCompleted") +
                    " targetVisits=" + ReadInt(searchProgram, "targetVisits") +
                    " pendingNode=" + ReadInt(searchProgram, "pendingNode") +
                    " searchToken=" + ReadInt(searchProgram, "searchToken") +
                    " searchError=" + ReadString(searchProgram, "lastError");
            if (encoderProgram != null)
                progress += " encoderState=" + ReadInt(encoderProgram, "encodeState") +
                " encoderStage=" + ReadInt(encoderProgram, "encodeStage") +
                " encoderCursor=" + ReadInt(encoderProgram, "encodeCursor") +
                " encoderError=" + ReadString(encoderProgram, "lastError") +
                " ladderStart=" + ReadInt(encoderProgram, "diagnosticLadderStart") +
                " ladderStage=" + ReadInt(encoderProgram, "diagnosticLadderStage") +
                " ladderDepth=" + ReadInt(encoderProgram, "diagnosticLadderDepth") +
                " ladderNodes=" + ReadInt(encoderProgram, "diagnosticLadderNodes") +
                " ladderTransitions=" + ReadInt(encoderProgram, "diagnosticLadderTransitions") +
                " ladderPlane=" + ReadInt(encoderProgram, "diagnosticLadderPlane") +
                " ladderActive=" + ReadBool(encoderProgram, "diagnosticLadderSearchActive");
            Fail(message + progress);
        }

        private static void ResolveDiagnosticPrograms()
        {
            if (aiProgram != null)
                return;
            GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoAiController controller = pool != null && pool.tableRoots != null &&
                pool.tableRoots.Length > 0 && pool.tableRoots[0] != null
                ? pool.tableRoots[0].GetComponentInChildren<GoAiController>(true)
                : UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (controller == null)
                return;
            aiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
            readerProgram = controller.reader == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.reader);
            searchProgram = controller.search == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.search);
            encoderProgram = controller.encoder == null ? null :
                UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.encoder);
        }

        private static void Fail(string message)
        {
            if (resultWritten) return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
