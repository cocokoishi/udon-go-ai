#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim guard for a non-seated spectator after a real ownership
    /// transfer. It builds a one-move PvP position, assigns the seat to a
    /// spawned remote player, transfers the table owner, then verifies that
    /// New Game, Force Reset and Undo are disabled and side-effect free.
    /// </summary>
    public static class GoClientSimPermissionGuardVerifier
    {
        private const double TimeoutSeconds = 120.0;
        private const string PendingSessionKey =
            "PureUdonGo.ClientSimPermissionGuardVerifier.Pending";
        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool remoteSpawnRequested;
        private static int stage;
        private static GoGame game;
        private static GoUI ui;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour probeProgram;
        private static VRCPlayerApi localPlayer;
        private static VRCPlayerApi remotePlayer;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(PendingSessionKey, false)) return;
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify Multiplayer Permission Guard")]
        public static void VerifyMultiplayerPermissionGuard()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded.");
            GoGeneratedWorld root = UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            ui = pool == null ? null : pool.primaryUI;
            game = ui == null ? null : ui.game;
            if (game == null)
                game = UnityEngine.Object.FindObjectOfType<GoGame>();
            if (ui == null)
                ui = UnityEngine.Object.FindObjectOfType<GoUI>();
            if (root == null || game == null || ui == null)
                throw new InvalidOperationException("Generated Go guard references were not found.");
            string programPath = GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoPermissionGuardProbe), "PermissionGuard");
            GameObject probeObject = new GameObject("ClientSim Permission Guard Probe");
            probeObject.transform.SetParent(root.transform, false);
            GoPermissionGuardProbe probe =
                probeObject.AddUdonSharpComponent<GoPermissionGuardProbe>();
            probe.game = game;
            probe.ui = ui;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe, programPath);
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim = true,
                displayLogs = true,
                deleteEditorOnly = false,
                spawnPlayer = true,
                hideMenuOnLaunch = true,
                setTargetFrameRate = false,
                localPlayerIsMaster = false,
                // Keep the local player as the initial table owner without
                // making them the instance master. The harness transfers the
                // owner to the spawned remote player before guard checks.
                isInstanceOwner = true,
                initializationDelay = 0f,
                currentLanguage = "en"
            });
            SessionState.SetBool(PendingSessionKey, true);
            EditorSceneManager.SetActiveScene(scene);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERMISSION_GUARD_START timeoutSeconds=" +
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
            remoteSpawnRequested = false;
            stage = 0;
            game = null;
            ui = null;
            gameProgram = null;
            uiProgram = null;
            probeProgram = null;
            localPlayer = null;
            remotePlayer = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PERMISSION_GUARD_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before spectator guard completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying) return;
            if (!enteredPlayMode) enteredPlayMode = true;
            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                ResolvePrograms();
                if (gameProgram == null || probeProgram == null)
                {
                    FailIfTimedOut("permission guard Udon programs were not found");
                    return;
                }
                localPlayer = Networking.LocalPlayer;
                if (stage == 0)
                {
                    if (!remoteSpawnRequested)
                    {
                        ClientSimMain.SpawnRemotePlayer("Remote Guard Owner");
                        remoteSpawnRequested = true;
                    }
                    stage = 1;
                    return;
                }
                if (stage == 1)
                {
                    remotePlayer = FindRemotePlayer();
                    if (remotePlayer == null || localPlayer == null)
                    {
                        FailIfTimedOut("remote guard player was not spawned");
                        return;
                    }
                    probeProgram.SetProgramVariable("remotePlayerId", remotePlayer.playerId);
                    probeProgram.SetProgramVariable("remotePlayerName", remotePlayer.displayName);
                    // ClientSim can auto-claim a generated PvP seat when the
                    // witness joins. Give the local authority back to the
                    // fixture before it builds the owned position; the next
                    // stage deliberately transfers both seat and ownership.
                    Networking.SetOwner(localPlayer, gameProgram.gameObject);
                    probeProgram.SendCustomEvent("PrepareOwnedPosition");
                    stage = 2;
                    return;
                }
                if (stage == 2)
                {
                    if (!ReadBool("prepared"))
                    {
                        FailIfTimedOut("owned PvP permission fixture was not prepared: " +
                            ReadString("failure"));
                        return;
                    }
                    probeProgram.SendCustomEvent("ReassignSeatToRemote");
                    stage = 3;
                    return;
                }
                if (stage == 3)
                {
                    if (!ReadBool("reassigned"))
                    {
                        FailIfTimedOut("remote PvP seat was not assigned");
                        return;
                    }
                    Networking.SetOwner(remotePlayer, gameProgram.gameObject);
                    stage = 4;
                    return;
                }
                if (stage == 4)
                {
                    VRCPlayerApi owner = Networking.GetOwner(gameProgram.gameObject);
                    if (owner == null || owner.playerId != remotePlayer.playerId)
                    {
                        FailIfTimedOut("remote table ownership did not apply");
                        return;
                    }
                    probeProgram.SendCustomEvent("RunSpectatorGuard");
                    stage = 5;
                    return;
                }
                if (stage == 5)
                {
                    if (!ReadBool("probeFinished"))
                    {
                        FailIfTimedOut("spectator guard probe did not finish");
                        return;
                    }
                    bool resetDenied = ReadBool("resetDenied");
                    bool newGameDenied = ReadBool("newGameDenied");
                    bool undoDenied = ReadBool("undoDenied");
                    bool resetDisabled = !ReadUiButtonState(40);
                    bool newGameDisabled = !ReadUiButtonState(0);
                    bool undoDisabled = !ReadUiButtonState(44);
                    bool pass = resetDenied && newGameDenied && undoDenied &&
                        resetDisabled && newGameDisabled && undoDisabled;
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERMISSION_GUARD_RESULT pass=" + pass +
                        " resetDenied=" + resetDenied + " newGameDenied=" + newGameDenied +
                        " undoDenied=" + undoDenied + " buttons=" + resetDisabled + "," +
                        newGameDisabled + "," + undoDisabled + " revision=" +
                        ReadInt("beforeRevision") + "->" + ReadInt("afterRevision") +
                        " moveCount=" + ReadInt("beforeMoveCount") + "->" +
                        ReadInt("afterMoveCount") + " failure=" + ReadString("failure"));
                    if (pass)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_PERMISSION_GUARD_PASS");
                        StopWithResult(true);
                    }
                    else
                    {
                        Fail("spectator permission guard failed: " + ReadString("failure"));
                    }
                }
            }
            catch (Exception exception)
            {
                Fail("permission guard inspection exception: " + exception);
            }
        }

        private static void ResolvePrograms()
        {
            // ClientSim reloads the generated scene into a play-mode copy;
            // editor references captured before entering play can therefore
            // compare equal to null. Re-resolve the live proxies first.
            GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if (pool != null && pool.primaryUI != null)
            {
                ui = pool.primaryUI;
                if (ui.game != null)
                    game = ui.game;
            }
            if (game == null)
                game = UnityEngine.Object.FindObjectOfType<GoGame>();
            if (ui == null)
                ui = UnityEngine.Object.FindObjectOfType<GoUI>();
            if (gameProgram == null && game != null)
                gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(game);
            if (uiProgram == null && ui != null)
                uiProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(ui);
            if (probeProgram == null)
            {
                GoPermissionGuardProbe probe =
                    UnityEngine.Object.FindObjectOfType<GoPermissionGuardProbe>();
                if (probe != null)
                    probeProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
            }
        }

        private static VRCPlayerApi FindRemotePlayer()
        {
            if (localPlayer == null) return null;
            var players = VRCPlayerApi.AllPlayers;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].playerId != localPlayer.playerId)
                    return players[i];
            return null;
        }

        private static bool ReadBool(string name)
        {
            object value = probeProgram == null ? null : probeProgram.GetProgramVariable(name);
            return value != null && Convert.ToBoolean(value);
        }

        private static int ReadInt(string name)
        {
            object value = probeProgram == null ? null : probeProgram.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static string ReadString(string name)
        {
            object value = probeProgram == null ? null : probeProgram.GetProgramVariable(name);
            return value == null ? "" : value.ToString();
        }

        private static bool ReadUiButtonState(int action)
        {
            if (uiProgram == null) return false;
            Button[] buttons = uiProgram.GetProgramVariable("controlButtons") as Button[];
            int[] actions = uiProgram.GetProgramVariable("controlButtonActions") as int[];
            if (buttons == null || actions == null) return false;
            int count = buttons.Length < actions.Length ? buttons.Length : actions.Length;
            for (int i = 0; i < count; i++)
                if (actions[i] == action)
                    return buttons[i] != null && buttons[i].interactable;
            return false;
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup >= deadline) Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten) return;
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_PERMISSION_GUARD_FAIL " + message);
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.Exit(1);
        }

        private static void StopWithResult(bool pass)
        {
            if (resultWritten) return;
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERMISSION_GUARD_RESULT pass=" + pass);
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
