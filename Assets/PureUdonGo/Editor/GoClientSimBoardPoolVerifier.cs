#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Exercises the generated table pool through real Unity Button.onClick
    /// events in ClientSim. The pool itself and each Go AI controller execute
    /// as serialized Udon programs; the editor only observes their variables.
    /// </summary>
    public static class GoClientSimBoardPoolVerifier
    {
        private const double TimeoutSeconds = 45.0;
        private const string PendingSessionKey = "PureUdonGo.ClientSimBoardPoolVerifier.Pending";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int stage;
        private static UdonBehaviour poolProgram;
        private static GoBoardPool poolProxy;
        private static GoUI primaryUi;
        private static GoUI secondUi;
        private static int dualSearches;
        private static int firstStride;
        private static int secondStride;
        private static int firstSlot;
        private static int secondSlot;
        private static int initialAllocatedWorkers;
        private static int initialAllocatedEdgeCapacity;
        private static int initialEstimatedSearchArrayBytes;
        private static int dualAllocatedWorkers;
        private static int dualAllocatedEdgeCapacity;
        private static int dualEstimatedSearchArrayBytes;
        private static int dualEstimatedGpuTextureBytes;

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_REATTACHED stage=" + stage);
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Board Pool")]
        public static void VerifyClientSimBoardPool()
        {
            ResetRunner();
            Scene scene = EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded.");

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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_START scene=" +
                Editor.GoWorldGenerator.ScenePath + " timeoutSeconds=" + TimeoutSeconds);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            SessionState.SetBool(PendingSessionKey, false);
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            stage = 0;
            poolProgram = null;
            poolProxy = null;
            primaryUi = null;
            secondUi = null;
            dualSearches = 0;
            firstStride = 0;
            secondStride = 0;
            firstSlot = 0;
            secondSlot = 0;
            initialAllocatedWorkers = 0;
            initialAllocatedEdgeCapacity = 0;
            initialEstimatedSearchArrayBytes = 0;
            dualAllocatedWorkers = 0;
            dualAllocatedEdgeCapacity = 0;
            dualEstimatedSearchArrayBytes = 0;
            dualEstimatedGpuTextureBytes = 0;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                     SessionState.GetBool(PendingSessionKey, false))
            {
                Fail("PlayMode ended before the board-pool regression completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_PLAYMODE_ENTERED");
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                if (!ResolvePool())
                {
                    FailIfTimedOut("generated GoBoardPool or primary/second GoUI was not found");
                    return;
                }

                if (stage == 0)
                {
                    ValidateInitialPool();
                    stage = 1;
                    return;
                }
                if (stage == 1)
                {
                    if (ReadInt(poolProgram, "visibleTableCount") == GoBoardPool.FIXED_VISIBLE_TABLES)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_STATE visible=3 fixed=true");
                        StartDualSearch();
                        stage = 3;
                        return;
                    }
                    FailIfTimedOut("fixed table endpoints changed visibleTableCount");
                    return;
                }
                if (stage == 3)
                {
                    int running = ReadInt(poolProgram, "localRunningSearches");
                    if (running >= 2)
                    {
                        int aiWarmupPoints = ReadInt(poolProgram,
                            "moveMaskWarmupPointsDuringAiSearch");
                        if (aiWarmupPoints != 0)
                        {
                            Fail("authoritative move-mask warm-up ran during AI search points=" +
                                aiWarmupPoints);
                            return;
                        }
                        ReadSchedulerState();
                        if (firstStride < 2 || secondStride < 2 || firstSlot == secondSlot)
                        {
                            Fail("two active Udon searches did not receive distinct fair scheduler slots: " +
                                "running=" + running + " stride=" + firstStride + "," + secondStride +
                                " slots=" + firstSlot + "," + secondSlot);
                            return;
                        }
                        dualSearches = running;
                        poolProgram.SendCustomEvent("RefreshMemoryAccounting");
                        dualAllocatedWorkers=ReadInt(poolProgram,"allocatedSearchWorkers");
                        dualAllocatedEdgeCapacity=ReadInt(poolProgram,"allocatedEdgeCapacity");
                        dualEstimatedSearchArrayBytes=ReadInt(poolProgram,"estimatedSearchArrayBytes");
                        dualEstimatedGpuTextureBytes=ReadInt(poolProgram,"estimatedGpuTextureBytes");
                        if(dualAllocatedWorkers<2||
                            dualAllocatedEdgeCapacity<GoMctsSearch.MAX_EDGES*2||
                            dualAllocatedEdgeCapacity>=GoMctsSearch.MAX_EDGES*GoBoardPool.MAX_TABLES)
                        {
                            // A second controller can become scheduler-active
                            // one frame before its lazy tree/GPU arrays finish
                            // allocating. Keep observing the real lifecycle;
                            // only the verifier deadline is a hard failure.
                            FailIfTimedOut("lazy AI allocation accounting did not reach two active workers workers="+
                                dualAllocatedWorkers+" edgeCapacity="+dualAllocatedEdgeCapacity);
                            return;
                        }
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_SCHEDULER running=" + running +
                            " stride=" + firstStride + "," + secondStride +
                            " slots=" + firstSlot + "," + secondSlot +
                            " dispatches=" + ReadInt(poolProgram, "localDispatchesPerFrame")+
                            " moveMaskWarmupPointsDuringAiSearch="+aiWarmupPoints);
                        StopDualSearch();
                        stage = 4;
                        return;
                    }
                    FailIfTimedOut("two visible Udon tables did not become active searches; running=" + running);
                    return;
                }
                if (stage == 4)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_PASS visible=3 fixed=true tables=" +
                        GoBoardPool.MAX_TABLES + " dualSearches=" + dualSearches +
                        " schedulerSlots=" + firstSlot + "," + secondSlot+
                        " initialWorkers="+initialAllocatedWorkers+
                        " initialEdgeCapacity="+initialAllocatedEdgeCapacity+
                        " initialSearchArrayBytes="+initialEstimatedSearchArrayBytes+
                        " dualWorkers="+dualAllocatedWorkers+
                        " dualEdgeCapacity="+dualAllocatedEdgeCapacity+
                        " dualSearchArrayBytes="+dualEstimatedSearchArrayBytes+
                        " dualGpuTextureBytes="+dualEstimatedGpuTextureBytes);
                    StopWithResult(true);
                }
            }
            catch (Exception exception)
            {
                Fail("board-pool ClientSim inspection exception: " + exception);
            }
        }

        private static bool ResolvePool()
        {
            if (poolProgram != null && primaryUi != null && secondUi != null)
                return true;
            poolProxy = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if (poolProxy == null)
                return false;
            poolProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(poolProxy);
            primaryUi = poolProxy.primaryUI;
            if (primaryUi == null)
                primaryUi = UnityEngine.Object.FindObjectOfType<GoUI>();
            GoUI[] uis = UnityEngine.Object.FindObjectsOfType<GoUI>(true);
            secondUi = GetTableUI(1);
            return poolProgram != null && primaryUi != null && secondUi != null;
        }

        private static GoUI GetTableUI(int index)
        {
            if (poolProxy == null || poolProxy.tableRoots == null ||
                index < 0 || index >= poolProxy.tableRoots.Length ||
                poolProxy.tableRoots[index] == null)
                return null;
            return poolProxy.tableRoots[index].GetComponentInChildren<GoUI>(true);
        }

        private static void ValidateInitialPool()
        {
            poolProgram.SendCustomEvent("RefreshMemoryAccounting");
            if (ReadInt(poolProgram, "visibleTableCount") != GoBoardPool.INITIAL_VISIBLE_TABLES)
                throw new InvalidOperationException("initial visible table count is not 3");
            GameObject[] roots = poolProxy.tableRoots;
            if (roots == null || roots.Length != GoBoardPool.MAX_TABLES)
                throw new InvalidOperationException("runtime tableRoots does not contain 16 entries");
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || roots[i].activeSelf != (i < GoBoardPool.INITIAL_VISIBLE_TABLES))
                    throw new InvalidOperationException("table visibility mismatch at index " + i);
            }
            GoGame firstGame = GetGame(0);
            GoGame secondGame = GetGame(1);
            GoAiController firstController = GetAiController(0);
            GoAiController secondController = GetAiController(1);
            if (firstGame == null || secondGame == null || firstController == null || secondController == null)
                throw new InvalidOperationException("independent table runtime components are incomplete");
            if (firstGame == secondGame || firstController == secondController)
                throw new InvalidOperationException("table 0 and table 1 share runtime state");
            initialAllocatedWorkers=ReadInt(poolProgram,"allocatedSearchWorkers");
            initialAllocatedEdgeCapacity=ReadInt(poolProgram,"allocatedEdgeCapacity");
            initialEstimatedSearchArrayBytes=ReadInt(poolProgram,"estimatedSearchArrayBytes");
            if(ReadInt(poolProgram,"tableCapacity")!=GoBoardPool.MAX_TABLES||
                initialAllocatedWorkers!=0||initialAllocatedEdgeCapacity!=0)
                throw new InvalidOperationException(
                    "16-table pool eagerly allocated heavy AI workers: workers="+
                    initialAllocatedWorkers+" edgeCapacity="+initialAllocatedEdgeCapacity);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_INITIAL visible=" +
                ReadInt(poolProgram, "visibleTableCount") + " capacity=" + roots.Length +
                " fixed=true active=0,1,2 independentRuntime=True allocatedWorkers="+
                initialAllocatedWorkers+" allocatedEdgeCapacity="+
                initialAllocatedEdgeCapacity+" estimatedSearchArrayBytes="+
                initialEstimatedSearchArrayBytes);
        }

        private static void StartDualSearch()
        {
            Press(FindButton(primaryUi, 6), "Primary AIvAI");
            Press(FindButton(secondUi, 6), "Second AIvAI");
            Press(FindButton(primaryUi, 34), "Primary Start Match");
            Press(FindButton(secondUi, 34), "Second Start Match");
            Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_EVENT dual=AIvAI tables=0,1");
        }

        private static void StopDualSearch()
        {
            Press(FindButton(primaryUi, 0), "Primary New Game");
            Press(FindButton(secondUi, 0), "Second New Game");
        }

        private static void ReadSchedulerState()
        {
            GoAiController first = GetAiController(0);
            GoAiController second = GetAiController(1);
            UdonBehaviour firstProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(first);
            UdonBehaviour secondProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(second);
            firstStride = ReadInt(firstProgram, "schedulerStride");
            secondStride = ReadInt(secondProgram, "schedulerStride");
            firstSlot = ReadInt(firstProgram, "schedulerSlot");
            secondSlot = ReadInt(secondProgram, "schedulerSlot");
        }

        private static GoGame GetGame(int index)
        {
            return poolProxy == null || poolProxy.tableRoots == null ||
                index < 0 || index >= poolProxy.tableRoots.Length || poolProxy.tableRoots[index] == null
                ? null : poolProxy.tableRoots[index].GetComponentInChildren<GoGame>(true);
        }

        private static GoAiController GetAiController(int index)
        {
            return poolProxy == null || poolProxy.tableRoots == null ||
                index < 0 || index >= poolProxy.tableRoots.Length || poolProxy.tableRoots[index] == null
                ? null : poolProxy.tableRoots[index].GetComponentInChildren<GoAiController>(true);
        }

        private static Button FindButton(GoUI ui, int action)
        {
            if (ui == null || ui.controlButtons == null || ui.controlButtonActions == null)
                throw new InvalidOperationException("GoUI control arrays are missing for action " + action);
            int count = ui.controlButtons.Length < ui.controlButtonActions.Length ?
                ui.controlButtons.Length : ui.controlButtonActions.Length;
            for (int i = 0; i < count; i++)
                if (ui.controlButtonActions[i] == action)
                    return ui.controlButtons[i];
            throw new InvalidOperationException("GoUI action binding is missing: " + action);
        }

        private static void Press(Button button, string label)
        {
            if (button == null || button.onClick == null ||
                button.onClick.GetPersistentEventCount() != 1)
                throw new InvalidOperationException("generated Button.onClick is incomplete: " + label);
            button.onClick.Invoke();
            Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_BUTTON label=" + label + " path=UnityButton.onClick");
        }

        private static int ReadInt(UdonBehaviour program, string name)
        {
            object value = program == null ? null : program.GetProgramVariable(name);
            return value == null ? 0 : Convert.ToInt32(value);
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
            SessionState.SetBool(PendingSessionKey, false);
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_POOL_FAIL " + message);
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.Exit(1);
        }

        private static void StopWithResult(bool passed)
        {
            if (resultWritten)
                return;
            resultWritten = true;
            SessionState.SetBool(PendingSessionKey, false);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_POOL_RESULT pass=" + passed);
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
#endif
