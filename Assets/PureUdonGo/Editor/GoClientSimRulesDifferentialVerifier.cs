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
    /// Runs the fixed long-history probe as serialized Udon in ClientSim and
    /// exports its trace. The independent Python comparator is the authority
    /// for expected transition values.
    /// </summary>
    public static class GoClientSimRulesDifferentialVerifier
    {
        private const double TimeoutSeconds = 180.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimRulesDifferentialVerifier.Pending";
        private const string OutputFolderName = "GoRulesTrace";

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Export ClientSim Rules Trace")]
        public static void ExportClientSimRulesTrace()
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
                typeof(GoRulesDifferentialProbe), "Rules-differential");
            GameObject probeObject = new GameObject("ClientSim Rules Differential Probe");
            probeObject.transform.SetParent(root.transform, false);
            GoRulesDifferentialProbe probe =
                probeObject.AddUdonSharpComponent<GoRulesDifferentialProbe>();
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds +
                " moves=" + GoRulesDifferentialProbe.MOVE_COUNT);
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
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before rules trace completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_PLAYMODE_ENTERED");
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
                    GoRulesDifferentialProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoRulesDifferentialProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon rules differential probe was not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_EVENT name=RunRulesDifferentialProbe");
                    probeProgram.SendCustomEvent("RunRulesDifferentialProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("Udon rules differential probe did not finish");
                    return;
                }

                bool passed = ReadBool("probePassed");
                string failure = ReadString("failure");
                string outputPath = WriteTrace(passed, failure);
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_RESULT passed=" + passed +
                    " moves=" + GoRulesDifferentialProbe.MOVE_COUNT +
                    " export=" + outputPath + " failure=" + failure);
                if (passed && HasExpectedArrays())
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_PASS moves=160 accepted=160 referenceComparison=not-performed");
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon rules differential trace failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim rules trace inspection exception: " + exception);
            }
        }

        private static bool HasExpectedArrays()
        {
            return ReadIntArray("moves") != null && ReadIntArray("moves").Length == GoRulesDifferentialProbe.MOVE_COUNT &&
                ReadBoolArray("accepted") != null && ReadBoolArray("accepted").Length == GoRulesDifferentialProbe.MOVE_COUNT &&
                ReadIntArray("moveCounts") != null && ReadIntArray("moveCounts").Length == GoRulesDifferentialProbe.MOVE_COUNT &&
                ReadIntArray("boardTrace") != null && ReadIntArray("boardTrace").Length == GoRulesDifferentialProbe.MOVE_COUNT * GoGame.AREA &&
                ReadIntArray("previousBoard1Trace") != null && ReadIntArray("previousBoard1Trace").Length == GoRulesDifferentialProbe.MOVE_COUNT * GoGame.AREA &&
                ReadIntArray("previousBoard2Trace") != null && ReadIntArray("previousBoard2Trace").Length == GoRulesDifferentialProbe.MOVE_COUNT * GoGame.AREA;
        }

        private static string WriteTrace(bool passed, string failure)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(projectRoot, "Assets", "udon-go-ai",
                "Benchmarks", "KaTrain", "ClientSimExports", OutputFolderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "go-rules-trace.json");
            Trace trace = new Trace
            {
                schema = "pure-udon-go.clientsim-rules-trace.v1",
                status = passed ? "CLIENTSIM_RULES_TRACE_PASS" : "CLIENTSIM_RULES_TRACE_FAIL",
                referenceComparison = "NOT_PERFORMED_BY_THIS_VERIFIER",
                rules = "Tromp-Taylor positional-superko area komi-7.5 multi-stone-suicide",
                moves = ReadIntArray("moves"),
                accepted = ReadBoolArray("accepted"),
                moveCounts = ReadIntArray("moveCounts"),
                sideToMove = ReadIntArray("sideToMove"),
                consecutivePasses = ReadIntArray("consecutivePasses"),
                blackCaptures = ReadIntArray("blackCaptures"),
                whiteCaptures = ReadIntArray("whiteCaptures"),
                koLocations = ReadIntArray("koLocations"),
                gameStates = ReadIntArray("gameStates"),
                positionHistoryCounts = ReadIntArray("positionHistoryCounts"),
                lastMoves = ReadIntArray("lastMoves"),
                boardTrace = ReadIntArray("boardTrace"),
                previousBoard1Trace = ReadIntArray("previousBoard1Trace"),
                previousBoard2Trace = ReadIntArray("previousBoard2Trace"),
                finalMoveCount = ReadInt("finalMoveCount"),
                finalSideToMove = ReadInt("finalSideToMove"),
                finalConsecutivePasses = ReadInt("finalConsecutivePasses"),
                finalBlackCaptures = ReadInt("finalBlackCaptures"),
                finalWhiteCaptures = ReadInt("finalWhiteCaptures"),
                finalKoLoc = ReadInt("finalKoLoc"),
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
            public bool[] accepted;
            public int[] moveCounts;
            public int[] sideToMove;
            public int[] consecutivePasses;
            public int[] blackCaptures;
            public int[] whiteCaptures;
            public int[] koLocations;
            public int[] gameStates;
            public int[] positionHistoryCounts;
            public int[] lastMoves;
            public int[] boardTrace;
            public int[] previousBoard1Trace;
            public int[] previousBoard2Trace;
            public int finalMoveCount;
            public int finalSideToMove;
            public int finalConsecutivePasses;
            public int finalBlackCaptures;
            public int finalWhiteCaptures;
            public int finalKoLoc;
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULE_TRACE_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
