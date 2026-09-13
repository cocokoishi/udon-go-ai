#if UNITY_EDITOR
using System;
using System.IO;
using UdonSharp;
using UdonSharp.Compiler;
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
    /// Exports feature planes produced by the compiled Udon encoder so the
    /// repository's independent KataGo V7 reference/oracle can compare them.
    /// </summary>
    public static class GoClientSimFeatureVerifier
    {
        private const double TimeoutSeconds = 900.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimFeatureVerifier.Pending";
        private static readonly string[] CaseNames =
        {
            "empty-tromp", "opening-7-tromp", "fight-15-tromp", "ladder-19-tromp",
            "capture-9-tromp", "pass-history-tromp", "double-pass-tromp", "empty-chinese",
            "random-32-tromp", "random-64-tromp-pass", "random-96-tromp", "random-144-tromp-pass",
            "random-32-chinese", "random-64-chinese-pass", "random-96-chinese", "random-144-chinese-pass"
        };

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Feature Equivalence")]
        public static void VerifyClientSimFeatureEquivalence()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: " +
                    Editor.GoWorldGenerator.ScenePath);

            GoGeneratedWorld rootMarker =
                UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoGame gameProxy = UnityEngine.Object.FindObjectOfType<GoGame>();
            GoFeatureEncoder encoderProxy = UnityEngine.Object.FindObjectOfType<GoFeatureEncoder>();
            GoAiController controllerProxy = UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (rootMarker == null || gameProxy == null || encoderProxy == null ||
                controllerProxy == null || gameProxy.blackDifficulty == null ||
                gameProxy.whiteDifficulty == null)
                throw new InvalidOperationException(
                    "Generated Go feature/runtime objects were not found");

            string probeProgramPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoFeatureProbe), "Feature");
            GameObject probeObject = new GameObject("ClientSim Feature Probe");
            probeObject.transform.SetParent(rootMarker.transform, false);
            GoFeatureProbe probe = probeObject.AddUdonSharpComponent<GoFeatureProbe>();
            probe.game = gameProxy;
            probe.encoder = encoderProxy;
            probe.controller = controllerProxy;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe, probeProgramPath);

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds +
                " cases=" + GoFeatureProbe.CASE_COUNT);
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
                Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before Udon feature probe completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_PLAYMODE_ENTERED");
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
                    GoFeatureProbe probe = UnityEngine.Object.FindObjectOfType<GoFeatureProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon feature probe was not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_EVENT name=RunFeatureProbe");
                    probeProgram.SendCustomEvent("RunFeatureProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool(probeProgram, "probeFinished"))
                {
                    FailIfTimedOut("Udon feature probe did not finish");
                    return;
                }

                bool passed = ReadBool(probeProgram, "probePassed");
                bool handicapSelfKomi = ReadBool(probeProgram, "handicapSelfKomiPassed");
                bool[] encoded = probeProgram.GetProgramVariable("encoded") as bool[];
                int[] moveCounts = probeProgram.GetProgramVariable("moveCounts") as int[];
                int[] sideToMove = probeProgram.GetProgramVariable("sideToMove") as int[];
                int[] gameStates = probeProgram.GetProgramVariable("gameStates") as int[];
                int[] consecutivePasses = probeProgram.GetProgramVariable("consecutivePasses") as int[];
                int[] koLocations = probeProgram.GetProgramVariable("koLocations") as int[];
                int[] sequences0 = probeProgram.GetProgramVariable("emptyMoves") as int[];
                int[] sequences1 = probeProgram.GetProgramVariable("openingMoves") as int[];
                int[] sequences2 = probeProgram.GetProgramVariable("fightMoves") as int[];
                int[] sequences3 = probeProgram.GetProgramVariable("ladderMoves") as int[];
                int[] sequences4 = probeProgram.GetProgramVariable("captureMoves") as int[];
                int[] sequences5 = probeProgram.GetProgramVariable("passHistoryMoves") as int[];
                int[] sequences6 = probeProgram.GetProgramVariable("doublePassMoves") as int[];
                int[] sequences7 = probeProgram.GetProgramVariable("emptyChineseMoves") as int[];
                int[] sequences8 = probeProgram.GetProgramVariable("randomMoves0") as int[];
                int[] sequences9 = probeProgram.GetProgramVariable("randomMoves1") as int[];
                int[] sequences10 = probeProgram.GetProgramVariable("randomMoves2") as int[];
                int[] sequences11 = probeProgram.GetProgramVariable("randomMoves3") as int[];
                int[] sequences12 = probeProgram.GetProgramVariable("randomMoves4") as int[];
                int[] sequences13 = probeProgram.GetProgramVariable("randomMoves5") as int[];
                int[] sequences14 = probeProgram.GetProgramVariable("randomMoves6") as int[];
                int[] sequences15 = probeProgram.GetProgramVariable("randomMoves7") as int[];
                int[][] sequences =
                {
                    sequences0, sequences1, sequences2, sequences3, sequences4, sequences5,
                    sequences6, sequences7, sequences8, sequences9, sequences10, sequences11,
                    sequences12, sequences13, sequences14, sequences15
                };
                float[] spatial = probeProgram.GetProgramVariable("spatial") as float[];
                float[] global = probeProgram.GetProgramVariable("global") as float[];
                int firstClearCount = ReadInt(probeProgram, "firstEncodeSpatialClearCount");
                int firstClearSlices = ReadInt(probeProgram, "firstEncodeSpatialClearSlices");
                bool firstFreshOwned = ReadBool(probeProgram, "firstEncodeUsedFreshOwnedSpatial");
                string failure = ReadString(probeProgram, "failure");
                string fixturePath = WriteFixture(
                    encoded, moveCounts, sideToMove, gameStates, consecutivePasses,
                    koLocations, sequences, spatial, global);
                Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_RESULT passed=" + passed +
                    " cases=" + GoFeatureProbe.CASE_COUNT + " fixture=" + fixturePath +
                    " handicapSelfKomi=" + handicapSelfKomi +
                    " failure=" + failure);
                if (passed && handicapSelfKomi && firstClearCount == 0 && firstClearSlices == 0 && firstFreshOwned &&
                    encoded != null && moveCounts != null && sideToMove != null &&
                    gameStates != null && consecutivePasses != null && koLocations != null &&
                    spatial != null && global != null && encoded.Length >= GoFeatureProbe.CASE_COUNT &&
                    moveCounts.Length >= GoFeatureProbe.CASE_COUNT &&
                    spatial.Length >= GoFeatureProbe.CASE_COUNT * GoFeatureEncoder.SPATIAL_COUNT &&
                    global.Length >= GoFeatureProbe.CASE_COUNT * GoFeatureEncoder.GLOBAL_CHANNELS)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_PASS cases=" + GoFeatureProbe.CASE_COUNT +
                        " spatialFloats=" + (GoFeatureProbe.CASE_COUNT * GoFeatureEncoder.SPATIAL_COUNT) +
                        " globalFloats=" + (GoFeatureProbe.CASE_COUNT * GoFeatureEncoder.GLOBAL_CHANNELS) +
                        " firstSpatialClearCount=" + firstClearCount +
                        " firstSpatialClearSlices=" + firstClearSlices);
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon feature probe failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim feature inspection exception: " + exception);
            }
        }

        private static string WriteFixture(
            bool[] encoded, int[] moveCounts, int[] sideToMove, int[] gameStates,
            int[] consecutivePasses, int[] koLocations, int[][] sequences,
            float[] spatial, float[] global)
        {
            Fixture[] fixtures = new Fixture[GoFeatureProbe.CASE_COUNT];
            for (int i = 0; i < fixtures.Length; i++)
            {
                int moveCount = moveCounts == null || i >= moveCounts.Length ? 0 : moveCounts[i];
                int[] moves = TrimMoves(sequences[i], moveCount);
                float[] caseSpatial = new float[GoFeatureEncoder.SPATIAL_COUNT];
                float[] caseGlobal = new float[GoFeatureEncoder.GLOBAL_CHANNELS];
                if (spatial != null)
                    Array.Copy(spatial, i * GoFeatureEncoder.SPATIAL_COUNT,
                        caseSpatial, 0, caseSpatial.Length);
                if (global != null)
                    Array.Copy(global, i * GoFeatureEncoder.GLOBAL_CHANNELS,
                        caseGlobal, 0, caseGlobal.Length);
                fixtures[i] = new Fixture
                {
                    name = CaseNames[i],
                    moveCount = moveCount,
                    sideToMove = sideToMove == null || i >= sideToMove.Length ? 0 : sideToMove[i],
                    gameState = gameStates == null || i >= gameStates.Length ? 0 : gameStates[i],
                    consecutivePasses = consecutivePasses == null || i >= consecutivePasses.Length ? 0 : consecutivePasses[i],
                    koLoc = koLocations == null || i >= koLocations.Length ? GoGame.NONE : koLocations[i],
                    rules = IsChineseCase(i) ? "Chinese" : "Tromp-Taylor",
                    moves = ToCoordinates(moves),
                    spatial = caseSpatial,
                    global = caseGlobal
                };
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(root, "go-udon-feature-fixtures.json");
            File.WriteAllText(path, JsonUtility.ToJson(new FixtureSet { fixtures = fixtures }, true));
            return path;
        }

        private static int[] TrimMoves(int[] source, int count)
        {
            if (count <= 0)
                return new int[0];
            if (source == null || source.Length < count)
                return null;
            int[] result = new int[count];
            Array.Copy(source, result, count);
            return result;
        }

        private static bool IsChineseCase(int index)
        {
            return index == 7 || index >= 12;
        }

        private static string[] ToCoordinates(int[] moves)
        {
            string[] result = new string[moves == null ? 0 : moves.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = Coordinate(moves[i]);
            return result;
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

        [Serializable]
        private sealed class FixtureSet
        {
            public Fixture[] fixtures;
        }

        [Serializable]
        private sealed class Fixture
        {
            public string name;
            public string rules;
            public int moveCount;
            public int sideToMove;
            public int gameState;
            public int consecutivePasses;
            public int koLoc;
            public string[] moves;
            public float[] spatial;
            public float[] global;
        }

        private static bool ReadBool(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static int ReadInt(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static string ReadString(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value == null ? "" : value.ToString();
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_FEATURE_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_FEATURE_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
