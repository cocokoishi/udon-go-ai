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
    /// Exports the public 8/20/48/120 search ladder from real Udon
    /// ClientSim. It does not call runtime Go methods directly from the
    /// editor and does not perform the external oracle comparison itself.
    /// </summary>
    public static class GoClientSimSearchCalibrationVerifier
    {
        private const double TimeoutSeconds = 1200.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimSearchCalibrationVerifier.Pending";
        private const string ScenarioLimitSessionKey =
            "PureUdonGo.ClientSimSearchCalibrationVerifier.ScenarioLimit";
        private const string ProfileLimitSessionKey =
            "PureUdonGo.ClientSimSearchCalibrationVerifier.ProfileLimit";
        private const string ProfileStartSessionKey =
            "PureUdonGo.ClientSimSearchCalibrationVerifier.ProfileStart";
        private const string OutputFolderName = "GoSearchCalibration";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;
        private static int requestedScenarioLimit;
        private static int requestedProfileStart;
        private static int requestedProfileLimit;
        private static int lastLoggedSample = -1;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false))
                return;
            requestedScenarioLimit = SessionState.GetInt(
                ScenarioLimitSessionKey, GoSearchCalibrationProbe.SCENARIO_COUNT);
            requestedProfileLimit = SessionState.GetInt(
                ProfileLimitSessionKey, GoSearchCalibrationProbe.PROFILE_COUNT);
            requestedProfileStart = SessionState.GetInt(ProfileStartSessionKey, 0);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Export ClientSim Search Calibration")]
        public static void ExportClientSimSearchCalibration()
        {
            StartExport(GoSearchCalibrationProbe.SCENARIO_COUNT,
                0, GoSearchCalibrationProbe.PROFILE_COUNT,
                Editor.GoWorldGenerator.ScenePath);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Export ClientSim Search Calibration 8-20")]
        public static void ExportClientSimSearchCalibration8And20()
        {
            StartExport(GoSearchCalibrationProbe.SCENARIO_COUNT, 0, 2,
                Editor.GoWorldGenerator.ScenePath);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Export ClientSim Search Calibration 48-120")]
        public static void ExportClientSimSearchCalibration48And120()
        {
            StartExport(GoSearchCalibrationProbe.SCENARIO_COUNT, 2,
                GoSearchCalibrationProbe.PROFILE_COUNT,
                Editor.GoWorldGenerator.ScenePath);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Export ClientSim Search Calibration (minimal fixture)")]
        public static void ExportClientSimSearchCalibrationMinimalFixture()
        {
            Editor.GoWorldGenerator.GenerateReaderRecoveryFixtureScene();
            StartExport(GoSearchCalibrationProbe.SCENARIO_COUNT, 0,
                GoSearchCalibrationProbe.PROFILE_COUNT,
                Editor.GoWorldGenerator.ReaderRecoveryScenePath);
        }

        private static void StartExport(int scenarioLimit, int profileStart,
            int profileLimit, string scenePath)
        {
            ResetRunner();
            requestedScenarioLimit = scenarioLimit;
            requestedProfileStart = profileStart;
            requestedProfileLimit = profileLimit;
            SessionState.SetInt(ScenarioLimitSessionKey, requestedScenarioLimit);
            SessionState.SetInt(ProfileStartSessionKey, requestedProfileStart);
            SessionState.SetInt(ProfileLimitSessionKey, requestedProfileLimit);
            Scene scene = EditorSceneManager.OpenScene(scenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated calibration scene was not loaded: " + scenePath);

            GoGeneratedWorld root = UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoGame game = pool != null && pool.tableRoots != null &&
                pool.tableRoots.Length > 0 && pool.tableRoots[0] != null
                ? pool.tableRoots[0].GetComponentInChildren<GoGame>(true)
                : UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller = game == null ? null :
                game.GetComponentInChildren<GoAiController>(true);
            if (game == null || controller == null ||
                game.blackDifficulty == null || game.whiteDifficulty == null)
                throw new InvalidOperationException(
                    "Generated Go calibration runtime objects were not found: " + scenePath);

            string probeAssetPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoSearchCalibrationProbe), "Search-calibration");
            GameObject probeObject = new GameObject("ClientSim Search Calibration Probe");
            probeObject.transform.SetParent(
                root != null ? root.transform : game.transform, false);
            GoSearchCalibrationProbe probe =
                probeObject.AddUdonSharpComponent<GoSearchCalibrationProbe>();
            probe.game = game;
            probe.controller = controller;
            probe.blackDifficulty = game.blackDifficulty;
            probe.whiteDifficulty = game.whiteDifficulty;
            probe.aiSettings = game.aiSettings;
            probe.scenarioLimit = requestedScenarioLimit;
            probe.profileStart = requestedProfileStart;
            probe.profileLimit = requestedProfileLimit;
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
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_START scene=" +
                scenePath + " timeoutSeconds=" + TimeoutSeconds +
                " samples=" + (requestedScenarioLimit *
                    (requestedProfileLimit - requestedProfileStart)) +
                " profileStart=" + requestedProfileStart +
                " profileLimit=" + requestedProfileLimit +
                " profiles=" + GoDifficultyProfile.BEGINNER_VISITS + "," +
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
            enteredPlayMode = false;
            resultWritten = false;
            probeSent = false;
            probeProgram = null;
            lastLoggedSample = -1;
            lastLoggedSample = -1;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before search calibration completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_PLAYMODE_ENTERED");
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
                    GoSearchCalibrationProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoSearchCalibrationProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon search calibration probe was not found");
                    return;
                }
                int currentSample = ReadInt("sampleIndex");
                if (currentSample != lastLoggedSample)
                {
                    lastLoggedSample = currentSample;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_PROGRESS sample=" +
                        currentSample + " scenario=" + ReadInt("scenarioIndex") +
                        " profile=" + ReadInt("profileIndex") +
                        " searchPhase=" + ReadInt("currentSearchPhase") +
                        " searchVisits=" + ReadInt("currentSearchVisits"));
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_EVENT name=RunSearchCalibrationProbe");
                    probeProgram.SendCustomEvent("RunSearchCalibrationProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("Udon search calibration probe did not finish");
                    return;
                }
                bool passed = ReadBool("probePassed");
                string failure = ReadString("failure");
                string outputPath = WriteTrace(passed, failure);
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_RESULT passed=" + passed +
                    " samples=" + (requestedScenarioLimit *
                        (requestedProfileLimit - requestedProfileStart)) +
                    " export=" + outputPath + " failure=" + failure);
                if (passed && HasExpectedArrays())
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_PASS samples=" +
                        (requestedScenarioLimit *
                            (requestedProfileLimit - requestedProfileStart)) +
                    " profiles=" + GoDifficultyProfile.BEGINNER_VISITS + "," +
                    GoDifficultyProfile.ADVANCED_VISITS + "," +
                    GoDifficultyProfile.MASTER_VISITS + "," +
                    GoDifficultyProfile.ULTRAHARD_VISITS +
                    " oracleComparison=not-performed");
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon search calibration probe failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim search calibration inspection exception: " + exception);
            }
        }

        private static bool HasExpectedArrays()
        {
            int count = GoSearchCalibrationProbe.SAMPLE_COUNT;
            return ReadIntArray("selectedMoves") != null && ReadIntArray("selectedMoves").Length == count &&
                ReadIntArray("policyMoves") != null && ReadIntArray("policyMoves").Length == count &&
                ReadIntArray("actualVisits") != null && ReadIntArray("actualVisits").Length == count &&
                ReadIntArray("targetSearchVisits") != null && ReadIntArray("targetSearchVisits").Length == count &&
                ReadIntArray("treeNodes") != null && ReadIntArray("treeNodes").Length == count &&
                ReadIntArray("treeEdges") != null && ReadIntArray("treeEdges").Length == count &&
                ReadIntArray("selectedRootVisits") != null && ReadIntArray("selectedRootVisits").Length == count &&
                ReadIntArray("readerStages") != null && ReadIntArray("readerStages").Length == count &&
                ReadIntArray("ownershipReadbacks") != null && ReadIntArray("ownershipReadbacks").Length == count &&
                ReadIntArray("rootOutputRevisions") != null && ReadIntArray("rootOutputRevisions").Length == count &&
                ReadIntArray("rootOutputSearchTokens") != null && ReadIntArray("rootOutputSearchTokens").Length == count &&
                ReadFloatArray("elapsedSeconds") != null && ReadFloatArray("elapsedSeconds").Length == count &&
                ReadFloatArray("rootMeanValues") != null && ReadFloatArray("rootMeanValues").Length == count;
        }

        private static string WriteTrace(bool passed, string failure)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(projectRoot, "Assets", "udon-go-ai",
                "Benchmarks", "KaTrain", "ClientSimExports", OutputFolderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "go-search-calibration.json");
            Trace trace = new Trace
            {
                schema = "pure-udon-go.clientsim-search-calibration.v1",
                status = passed ? "CLIENTSIM_SEARCH_CALIBRATION_PASS" : "CLIENTSIM_SEARCH_CALIBRATION_FAIL",
                oracleComparison = "NOT_PERFORMED_BY_THIS_VERIFIER",
                model = GoProductionModelLayout.ModelName,
                rules = "Tromp-Taylor positional-superko area komi-7.5",
                scenarioLimit = ReadInt("scenarioLimit"),
                profileStart = ReadInt("profileStart"),
                profileLimit = ReadInt("profileLimit"),
                sampleCount = ReadInt("scenarioLimit") *
                    (ReadInt("profileLimit") - ReadInt("profileStart")),
                targetVisits = ReadIntArray("targetVisits"),
                selectedMoves = ReadIntArray("selectedMoves"),
                policyMoves = ReadIntArray("policyMoves"),
                actualVisits = ReadIntArray("actualVisits"),
                targetSearchVisits = ReadIntArray("targetSearchVisits"),
                moveCounts = ReadIntArray("moveCounts"),
                treeNodes = ReadIntArray("treeNodes"),
                treeEdges = ReadIntArray("treeEdges"),
                selectedRootVisits = ReadIntArray("selectedRootVisits"),
                readerStages = ReadIntArray("readerStages"),
                ownershipReadbacks = ReadIntArray("ownershipReadbacks"),
                rootOutputRevisions = ReadIntArray("rootOutputRevisions"),
                rootOutputSearchTokens = ReadIntArray("rootOutputSearchTokens"),
                startFrames = ReadIntArray("startFrames"),
                endFrames = ReadIntArray("endFrames"),
                elapsedSeconds = ReadFloatArray("elapsedSeconds"),
                rootMeanValues = ReadFloatArray("rootMeanValues"),
                failure = failure ?? ""
            };
            File.WriteAllText(path, JsonUtility.ToJson(trace, false));
            return path;
        }

        [Serializable]
        private sealed class Trace
        {
            public string schema;
            public string status;
            public string oracleComparison;
            public string model;
            public string rules;
            public int scenarioLimit;
            public int profileStart;
            public int profileLimit;
            public int sampleCount;
            public int[] targetVisits;
            public int[] selectedMoves;
            public int[] policyMoves;
            public int[] actualVisits;
            public int[] targetSearchVisits;
            public int[] moveCounts;
            public int[] treeNodes;
            public int[] treeEdges;
            public int[] selectedRootVisits;
            public int[] readerStages;
            public int[] ownershipReadbacks;
            public int[] rootOutputRevisions;
            public int[] rootOutputSearchTokens;
            public int[] startFrames;
            public int[] endFrames;
            public float[] elapsedSeconds;
            public float[] rootMeanValues;
            public string failure;
        }

        private static int ReadInt(string name)
        {
            object value = probeProgram.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static int[] ReadIntArray(string name)
        {
            return probeProgram.GetProgramVariable(name) as int[];
        }

        private static float[] ReadFloatArray(string name)
        {
            return probeProgram.GetProgramVariable(name) as float[];
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
                Fail(message + " sample=" + ReadInt("sampleIndex") +
                    " scenario=" + ReadInt("scenarioIndex") +
                    " profile=" + ReadInt("profileIndex") +
                    " searchPhase=" + ReadInt("currentSearchPhase") +
                    " searchVisits=" + ReadInt("currentSearchVisits"));
        }

        private static void Fail(string message)
        {
            if (resultWritten)
                return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            SessionState.SetInt(ScenarioLimitSessionKey, 0);
            SessionState.SetInt(ProfileStartSessionKey, 0);
            SessionState.SetInt(ProfileLimitSessionKey, 0);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
