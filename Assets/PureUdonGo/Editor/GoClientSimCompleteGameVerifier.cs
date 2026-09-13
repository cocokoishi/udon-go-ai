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
    /// Runs a long AIvAI game through the real serialized Udon programs in
    /// ClientSim. The editor side only installs the temporary probe, sends one
    /// Udon event and reads its result.
    /// </summary>
    public static class GoClientSimCompleteGameVerifier
    {
        private const double TimeoutSeconds = 1200.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimCompleteGameSmokeVerifier.Pending";

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Complete AI Game Smoke")]
        public static void VerifyClientSimCompleteAiGame()
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
                typeof(GoCompleteGameProbe), "Complete-game-smoke");
            GameObject probeObject = new GameObject("ClientSim Complete Game Probe");
            probeObject.transform.SetParent(rootMarker.transform, false);
            GoCompleteGameProbe probe = probeObject.AddUdonSharpComponent<GoCompleteGameProbe>();
            probe.game = gameProxy;
            probe.controller = controllerProxy;
            probe.blackDifficulty = gameProxy.blackDifficulty;
            probe.whiteDifficulty = gameProxy.whiteDifficulty;
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds +
                " profile=Custom1x1visit");
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
                Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before complete AI game smoke finished");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_PLAYMODE_ENTERED");
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
                    GoCompleteGameProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoCompleteGameProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon complete-game probe was not found");
                    return;
                }
                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_EVENT name=RunCompleteGame");
                    probeProgram.SendCustomEvent("RunCompleteGame");
                    probeSent = true;
                    return;
                }
                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("Udon AI complete-game smoke probe did not finish");
                    return;
                }

                bool passed = ReadBool("probePassed");
                int moves = ReadInt("moveCountAtFinish");
                int aiMoves = ReadInt("aiMovesAtFinish");
                int passes = ReadInt("passMoves");
                int terminal = ReadInt("terminalState");
                int winner = ReadInt("winner");
                string failure = ReadString("failure");
                int controllerState = FindControllerState();
                string controllerError = FindControllerError();
                Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_RESULT passed=" + passed +
                    " moves=" + moves + " aiMoves=" + aiMoves + " passMoves=" + passes +
                    " terminalState=" + terminal + " winner=" + winner +
                    " controllerState=" + controllerState + " failure=" + failure);
                if (passed && moves >= 3 && aiMoves > 0 && passes >= 2 &&
                    terminal != GoGame.STATE_PLAYING &&
                    controllerState != GoAiController.STATE_ERROR)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_PASS strengthCalibration=not-performed");
                    StopWithResult(true);
                }
                else
                {
                    Fail("complete AI game smoke failed: " + failure +
                        " controllerError=" + controllerError);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim complete-game smoke inspection exception: " + exception);
            }
        }

        private static int FindControllerState()
        {
            GoAiController controller = UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (controller == null)
                return GoAiController.STATE_ERROR;
            UdonBehaviour program = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
            return ReadInt(program, "controllerState");
        }

        private static string FindControllerError()
        {
            GoAiController controller = UnityEngine.Object.FindObjectOfType<GoAiController>();
            if (controller == null)
                return "controller missing";
            UdonBehaviour program = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
            return ReadString(program, "lastError");
        }

        private static bool ReadBool(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value != null && Convert.ToBoolean(value);
        }

        private static int ReadInt(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static int ReadInt(UdonBehaviour program, string variableName)
        {
            object value = program.GetProgramVariable(variableName);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static string ReadString(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value == null ? "" : value.ToString();
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_COMPLETE_SMOKE_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
