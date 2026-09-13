#if UNITY_EDITOR
using System;
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
    /// Runs fixed PUCT-behaviour smoke samples through the real serialized Udon
    /// programs in ClientSim. It never calls GoGame/GoAiController methods
    /// directly from the editor; it only installs and signals the Udon probe.
    /// </summary>
    public static class GoClientSimPositionQualityVerifier
    {
        private const double TimeoutSeconds = 600.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimPuctSmokeVerifier.Pending";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;
        private static UdonBehaviour controllerProgram;
        private static UdonBehaviour searchProgram;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim PUCT Search Smoke")]
        public static void VerifyClientSimPositionQuality()
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
            GoAiController controllerProxy =
                UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (rootMarker == null || gameProxy == null || controllerProxy == null ||
                gameProxy.blackDifficulty == null || gameProxy.whiteDifficulty == null)
                throw new InvalidOperationException(
                    "Generated Go runtime objects or difficulty profiles were not found");

            string probeProgramPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoPositionQualityProbe), "Puct-smoke");
            GameObject probeObject = new GameObject("ClientSim Position Quality Probe");
            probeObject.transform.SetParent(rootMarker.transform, false);
            GoPositionQualityProbe probe = probeObject.AddUdonSharpComponent<GoPositionQualityProbe>();
            probe.game = gameProxy;
            probe.controller = controllerProxy;
            probe.blackDifficulty = gameProxy.blackDifficulty;
            probe.whiteDifficulty = gameProxy.whiteDifficulty;
            probe.aiSettings = gameProxy.aiSettings;
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds +
                " scenarios=4 visits=" + GoPositionQualityProbe.DIAGNOSTIC_VISITS);
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
            controllerProgram = null;
            searchProgram = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before PUCT smoke probe completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                ResolvePrograms();
                if (probeProgram == null || controllerProgram == null)
                {
                    FailIfTimedOut("generated Udon PUCT smoke programs were not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_EVENT name=RunPositionQualityProbe");
                    probeProgram.SendCustomEvent("RunPositionQualityProbe");
                    probeSent = true;
                    return;
                }
                if (!ReadBool(probeProgram, "probeFinished"))
                {
                    if (ReadInt(controllerProgram, "controllerState") == GoAiController.STATE_ERROR)
                    {
                        Fail("AI controller error=" + ReadString(controllerProgram, "lastError"));
                        return;
                    }
                    FailIfTimedOut("Udon PUCT smoke probe did not finish");
                    return;
                }

                bool passed = ReadBool(probeProgram, "probePassed");
                int[] selected = ReadIntArray(probeProgram, "selectedMoves");
                int[] policyMoves = ReadIntArray(probeProgram, "policyMoves");
                int[] visits = ReadIntArray(probeProgram, "actualVisits");
                int[] moveCounts = ReadIntArray(probeProgram, "moveCounts");
                int[] aiDeltas = ReadIntArray(probeProgram, "aiMoveDeltas");
                int[] treeNodes = ReadIntArray(probeProgram, "treeNodes");
                int[] treeEdges = ReadIntArray(probeProgram, "treeEdges");
                int[] readerStages = ReadIntArray(probeProgram, "readerStages");
                int[] ownershipReadbacks = ReadIntArray(probeProgram, "ownershipReadbacks");
                int[] scenario0 = ReadIntArray(probeProgram, "scenario0Moves");
                int[] scenario1 = ReadIntArray(probeProgram, "scenario1Moves");
                int[] scenario2 = ReadIntArray(probeProgram, "scenario2Moves");
                int[] scenario3 = ReadIntArray(probeProgram, "scenario3Moves");
                int[][] sequences = { scenario0, scenario1, scenario2, scenario3 };
                string[] names = { "opening", "capture", "fight", "mixed" };
                for (int i = 0; i < 4; i++)
                {
                    int move = selected != null && i < selected.Length ? selected[i] : GoGame.NONE;
                    int policyMove = policyMoves != null && i < policyMoves.Length ? policyMoves[i] : GoGame.NONE;
                    int sampleVisits = visits != null && i < visits.Length ? visits[i] : 0;
                    int count = moveCounts != null && i < moveCounts.Length ? moveCounts[i] : 0;
                    int delta = aiDeltas != null && i < aiDeltas.Length ? aiDeltas[i] : 0;
                    int nodes = treeNodes != null && i < treeNodes.Length ? treeNodes[i] : 0;
                    int edges = treeEdges != null && i < treeEdges.Length ? treeEdges[i] : 0;
                    int stages = readerStages != null && i < readerStages.Length ? readerStages[i] : 0;
                    int ownership = ownershipReadbacks != null && i < ownershipReadbacks.Length ? ownershipReadbacks[i] : 0;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_SAMPLE scenario=" + names[i] +
                        " fixedMoves=" + DescribeSequence(sequences[i]) +
                        " sideToMove=" + ((sequences[i] != null && (sequences[i].Length & 1) == 0) ? "BLACK" : "WHITE") +
                        " selectedMove=" + Coordinate(move) + " policyMove=" + Coordinate(policyMove) +
                        " policyAgreesWithPuct=" + (move == policyMove) + " actualVisits=" + sampleVisits +
                        " moveCount=" + count + " aiMoveDelta=" + delta +
                        " treeNodes=" + nodes + " treeEdges=" + edges +
                        " readerCompletedStages=" + stages + " ownershipReadbacks=" + ownership +
                        " " + DescribeRootStats(searchProgram));
                }
                string failure = ReadString(probeProgram, "failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_RESULT passed=" + passed +
                    " failure=" + failure);
                bool policyPuctDivergence = false;
                if (selected != null && policyMoves != null)
                    for (int i = 0; i < 4 && i < selected.Length && i < policyMoves.Length; i++)
                        if (selected[i] != policyMoves[i]) policyPuctDivergence = true;
                if (passed && selected != null && policyMoves != null && visits != null && moveCounts != null &&
                    aiDeltas != null && selected.Length >= 4 && visits.Length >= 4 &&
                    moveCounts.Length >= 4 && aiDeltas.Length >= 4 &&
                    treeNodes != null && treeEdges != null && readerStages != null &&
                    ownershipReadbacks != null && treeNodes.Length >= 4 && treeEdges.Length >= 4 &&
                    readerStages.Length >= 4 && ownershipReadbacks.Length >= 4 &&
                    visits[0] >= GoPositionQualityProbe.DIAGNOSTIC_VISITS &&
                    visits[1] >= GoPositionQualityProbe.DIAGNOSTIC_VISITS &&
                    visits[2] >= GoPositionQualityProbe.DIAGNOSTIC_VISITS &&
                    visits[3] >= GoPositionQualityProbe.DIAGNOSTIC_VISITS &&
                    readerStages[0] == 1 && readerStages[1] == 1 && readerStages[2] == 1 && readerStages[3] == 1 &&
                    ownershipReadbacks[0] >= 1 && ownershipReadbacks[1] >= 1 &&
                    ownershipReadbacks[2] >= 1 && ownershipReadbacks[3] >= 1 &&
                    aiDeltas[0] == 1 && aiDeltas[1] == 1 && aiDeltas[2] == 1 && aiDeltas[3] == 1)
                {
                    if (!policyPuctDivergence)
                    {
                        Fail("Udon PUCT smoke corpus did not expose a policy/PUCT divergence");
                        return;
                    }
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_PASS scenarios=4 visits=" +
                        GoPositionQualityProbe.DIAGNOSTIC_VISITS +
                        " policyPuctDivergence=true strengthCalibration=not-performed");
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon PUCT smoke probe failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim PUCT smoke inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            if (probeProgram == null)
            {
                GoPositionQualityProbe probe =
                    UnityEngine.Object.FindObjectOfType<GoPositionQualityProbe>();
                if (probe != null)
                    probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
            }
            if (controllerProgram == null)
            {
                GoAiController controller = UnityEngine.Object.FindObjectOfType<GoAiController>();
                if (controller != null)
                {
                    controllerProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
                    if (controller.search != null)
                        searchProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.search);
                }
            }
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

        private static int[] ReadIntArray(UdonBehaviour program, string variableName)
        {
            return program.GetProgramVariable(variableName) as int[];
        }

        private static string DescribeSequence(int[] moves)
        {
            if (moves == null)
                return "null";
            string result = "";
            for (int i = 0; i < moves.Length; i++)
            {
                if (i > 0)
                    result += ",";
                result += Coordinate(moves[i]);
            }
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

        private static string DescribeRootStats(UdonBehaviour program)
        {
            if (program == null)
                return "rootStats=unavailable";
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PUCT_SMOKE_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
