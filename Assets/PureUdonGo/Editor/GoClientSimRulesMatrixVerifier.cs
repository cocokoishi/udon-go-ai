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
    /// Executes the legal-move matrix probe as serialized Udon in ClientSim.
    /// The verifier only exports observations; compare_clientsim_legal_matrix.py
    /// independently replays each checkpoint and owns the expected masks.
    /// </summary>
    public static class GoClientSimRulesMatrixVerifier
    {
        private const double TimeoutSeconds = 180.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimRulesMatrixVerifier.Pending";
        private const string OutputFolderName = "GoRulesMatrix";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Legal-Move Matrix")]
        public static void ExportClientSimLegalMoveMatrix()
        {
            ResetRunner();
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
            if (root == null || game == null)
                throw new InvalidOperationException(
                    "Generated Go world or first table game was not found");

            string probeAssetPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoRulesLegalMatrixProbe), "Rules-legal-matrix");
            GameObject probeObject = new GameObject("ClientSim Rules Legal Matrix Probe");
            probeObject.transform.SetParent(root.transform, false);
            GoRulesLegalMatrixProbe probe =
                probeObject.AddUdonSharpComponent<GoRulesLegalMatrixProbe>();
            probe.game = game;
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " actions=" +
                GoRulesLegalMatrixProbe.ACTION_COUNT + " positions=" +
                GoRulesLegalMatrixProbe.POSITION_COUNT + " timeoutSeconds=" +
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
            probeSent = false;
            probeProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before legal-move matrix completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_PLAYMODE_ENTERED");
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
                    GoRulesLegalMatrixProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoRulesLegalMatrixProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon legal-matrix probe was not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_EVENT name=RunRulesLegalMatrixProbe");
                    probeProgram.SendCustomEvent("RunRulesLegalMatrixProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("Udon legal-move matrix probe did not finish");
                    return;
                }

                bool passed = ReadBool("probePassed");
                string failure = ReadString("failure");
                string outputPath = WriteTrace(passed, failure);
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_RESULT passed=" + passed +
                    " actions=" + GoRulesLegalMatrixProbe.ACTION_COUNT +
                    " positions=" + GoRulesLegalMatrixProbe.POSITION_COUNT +
                    " export=" + outputPath + " failure=" + failure);
                if (passed && HasExpectedArrays())
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_PASS actions=160 positions=11 legalQueries=3971 referenceComparison=not-performed");
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon legal-move matrix trace failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim legal-move matrix inspection exception: " + exception);
            }
        }

        private static bool HasExpectedArrays()
        {
            return ReadIntArray("moves") != null &&
                ReadIntArray("moves").Length == GoRulesLegalMatrixProbe.ACTION_COUNT &&
                ReadIntArray("checkpoints") != null &&
                ReadIntArray("checkpoints").Length == GoRulesLegalMatrixProbe.POSITION_COUNT &&
                ReadBoolArray("accepted") != null &&
                ReadBoolArray("accepted").Length == GoRulesLegalMatrixProbe.ACTION_COUNT &&
                ReadIntArray("boardTrace") != null &&
                ReadIntArray("boardTrace").Length == GoRulesLegalMatrixProbe.POSITION_COUNT * GoGame.AREA &&
                ReadBoolArray("legalTrace") != null &&
                ReadBoolArray("legalTrace").Length == GoRulesLegalMatrixProbe.POSITION_COUNT * GoGame.AREA &&
                ReadBoolArray("passLegalTrace") != null &&
                ReadBoolArray("passLegalTrace").Length == GoRulesLegalMatrixProbe.POSITION_COUNT;
        }

        private static string WriteTrace(bool passed, string failure)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(projectRoot, "Assets", "udon-go-ai",
                "Benchmarks", "KaTrain", "ClientSimExports", OutputFolderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "go-rules-legal-matrix.json");
            Trace trace = new Trace
            {
                schema = "pure-udon-go.clientsim-rules-legal-matrix.v1",
                status = passed ? "CLIENTSIM_RULES_LEGAL_MATRIX_PASS" : "CLIENTSIM_RULES_LEGAL_MATRIX_FAIL",
                referenceComparison = "NOT_PERFORMED_BY_THIS_VERIFIER",
                rules = "Tromp-Taylor positional-superko area komi-7.5 multi-stone-suicide",
                moves = ReadIntArray("moves"),
                checkpoints = ReadIntArray("checkpoints"),
                accepted = ReadBoolArray("accepted"),
                moveCounts = ReadIntArray("moveCounts"),
                sideToMove = ReadIntArray("sideToMove"),
                gameStates = ReadIntArray("gameStates"),
                positionHistoryCounts = ReadIntArray("positionHistoryCounts"),
                boardTrace = ReadIntArray("boardTrace"),
                passLegalTrace = ReadBoolArray("passLegalTrace"),
                legalTrace = ReadBoolArray("legalTrace"),
                legalCounts = ReadIntArray("legalCounts"),
                finalMoveCount = ReadInt("finalMoveCount"),
                finalSideToMove = ReadInt("finalSideToMove"),
                finalGameState = ReadInt("finalGameState"),
                finalPositionHistoryCount = ReadInt("finalPositionHistoryCount"),
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
            public string referenceComparison;
            public string rules;
            public int[] moves;
            public int[] checkpoints;
            public bool[] accepted;
            public int[] moveCounts;
            public int[] sideToMove;
            public int[] gameStates;
            public int[] positionHistoryCounts;
            public int[] boardTrace;
            public bool[] passLegalTrace;
            public bool[] legalTrace;
            public int[] legalCounts;
            public int finalMoveCount;
            public int finalSideToMove;
            public int finalGameState;
            public int finalPositionHistoryCount;
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

        private static bool[] ReadBoolArray(string name)
        {
            return probeProgram.GetProgramVariable(name) as bool[];
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
                Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten)
                return;
            resultWritten = true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_FINAL pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
