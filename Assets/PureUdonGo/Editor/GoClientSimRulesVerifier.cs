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
    /// Adds a temporary UdonSharp rules probe to an unsaved copy of the
    /// production scene and executes it in ClientSim. The probe calls the
    /// actual GoGame Udon methods, so the editor harness never edits board
    /// arrays or substitutes a managed rules implementation.
    /// </summary>
    public static class GoClientSimRulesVerifier
    {
        private const double TimeoutSeconds = 90.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimRulesVerifier.Pending";

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Go Rules")]
        public static void VerifyClientSimGoRules()
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
            GoSearchState searchProxy =
                UnityEngine.Object.FindObjectOfType<GoSearchState>();
            if (rootMarker == null || gameProxy == null || controllerProxy == null ||
                searchProxy == null)
                throw new InvalidOperationException("Generated Go runtime objects were not found");

            string probeProgramPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoRulesProbe), "Rules");
            GameObject probeObject = new GameObject("ClientSim Rules Probe");
            probeObject.transform.SetParent(rootMarker.transform, false);
            GoRulesProbe probe = probeObject.AddUdonSharpComponent<GoRulesProbe>();
            probe.game = gameProxy;
            probe.controller = controllerProxy;
            probe.searchState = searchProxy;
            probe.view = gameProxy.view;
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" +
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
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before the Udon rules probe completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;

            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_PLAYMODE_ENTERED");
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
                    GoRulesProbe probe =
                        UnityEngine.Object.FindObjectOfType<GoRulesProbe>();
                    if (probe != null)
                        probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if (probeProgram == null)
                {
                    FailIfTimedOut("temporary Udon rules probe was not found");
                    return;
                }

                if (!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_EVENT name=RunRulesProbe");
                    probeProgram.SendCustomEvent("RunRulesProbe");
                    probeSent = true;
                    return;
                }

                if (!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("Udon rules probe did not finish");
                    return;
                }

                bool capture = ReadBool("capturePassed");
                bool doublePass = ReadBool("doublePassPassed");
                bool exactAreaScore = ReadBool("exactAreaScorePassed");
                bool suicide = ReadBool("suicidePassed");
                bool ko = ReadBool("koPassed");
                bool simpleKo = ReadBool("simpleKoPassed");
                bool searchKo = ReadBool("searchKoPassed");
                bool positionalSuperko = ReadBool("positionalSuperkoPassed");
                bool multiSuicidePrisoner = ReadBool("multiSuicidePrisonerPassed");
                bool pureRulesApi = ReadBool("pureRulesQueryPassed");
                bool capacity = ReadBool("capacityPassed");
                bool handicap = ReadBool("handicapPassed");
                bool resign = ReadBool("resignPassed");
                bool previewFinalFields = ReadBool("previewFinalFieldsPassed");
                bool capturePresentation = ReadBool("capturePresentationPassed");
                bool multiCapturePresentation=ReadBool("multiCapturePresentationPassed");
                bool newGameNoCapture=ReadBool("newGameNoCapturePresentationPassed");
                bool forceResetNoCapture=ReadBool("forceResetNoCapturePresentationPassed");
                bool deserializeNoCapture=ReadBool("deserializeNoCapturePresentationPassed");
                int captureMoves = ReadInt("captureMoveCount");
                int terminalState = ReadInt("doublePassGameState");
                int doublePassBlackArea = ReadInt("doublePassBlackArea");
                int doublePassWhiteArea = ReadInt("doublePassWhiteArea");
                float doublePassScore = ReadFloat("doublePassScore");
                int suicideMoves = ReadInt("suicideMoveCount");
                int koLocation = ReadInt("koLocation");
                int koMoveCount = ReadInt("koMoveCount");
                int whitePrisonersAfterSuicide = ReadInt("whitePrisonersAfterSuicide");
                int handicapCount = ReadInt("handicapCount");
                int resignState = ReadInt("resignState");
                string failure = ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_RESULT capture=" + capture +
                    " doublePass=" + doublePass + " suicide=" + suicide +
                    " exactAreaScore=" + exactAreaScore +
                    " ko=" + ko + " simpleKo=" + simpleKo + " searchKo=" + searchKo +
                    " positionalSuperko=" + positionalSuperko +
                    " multiSuicidePrisoner=" + multiSuicidePrisoner +
                     " pureRulesApi=" + pureRulesApi + " capacity=" + capacity +
                     " handicap=" + handicap + " resign=" + resign +
                     " previewFinalFields=" + previewFinalFields +
                    " capturePresentation=" + capturePresentation +
                    " captureProbe=" + ReadInt("captureProbeStartCount") + ":" +
                    ReadInt("captureProbeLastStoneCount") + ":" +
                    ReadInt("captureProbeLastRefreshReason") + ":" +
                    ReadBool("captureProbeActive") +
                    " multiCapturePresentation="+multiCapturePresentation+
                    " newGameNoCapture="+newGameNoCapture+
                    " forceResetNoCapture="+forceResetNoCapture+
                    " deserializeNoCapture="+deserializeNoCapture+
                    " captureMoves=" + captureMoves + " terminalState=" +
                    terminalState + " suicideMoves=" + suicideMoves +
                    " doublePassArea=" + doublePassBlackArea + ":" + doublePassWhiteArea +
                    " doublePassScore=" + doublePassScore +
                    " koLocation=" + koLocation + " koMoveCount=" + koMoveCount +
                    " whitePrisonersAfterSuicide=" + whitePrisonersAfterSuicide +
                    " handicapCount=" + handicapCount + " resignState=" + resignState +
                    " failure=" + failure);
                if (capture && capturePresentation && multiCapturePresentation&&
                    newGameNoCapture&&forceResetNoCapture&&deserializeNoCapture&&
                    doublePass && exactAreaScore && suicide && ko && simpleKo && searchKo && positionalSuperko &&
                     multiSuicidePrisoner && pureRulesApi && capacity && handicap && resign &&
                     previewFinalFields)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_PASS");
                    StopWithResult(true);
                }
                else
                {
                    Fail("Udon rules probe failed: " + failure);
                }
            }
            catch (Exception exception)
            {
                Fail("ClientSim rules inspection exception: " + exception);
            }
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

        private static float ReadFloat(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static string ReadString(string variableName)
        {
            object value = probeProgram.GetProgramVariable(variableName);
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
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_RULES_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RULES_RESULT pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
