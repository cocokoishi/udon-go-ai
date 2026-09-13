#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace PureUdonGo
{
    /// <summary>
    /// Minimal cross-engine pilot harness.  The production Udon table plays
    /// one side through its real AI/controller path while an authorized,
    /// same-model KataGo analysis process supplies the other side through the
    /// real human seat/click path.  This is intentionally a pilot, not an Elo
    /// claim: statistically meaningful Elo requires many paired games.
    /// </summary>
    public static class GoClientSimEloPilotVerifier
    {
        private const double TimeoutSeconds = 1200.0;
        private const string OutputFolder = "Benchmarks/KaTrain/EloPilot";
        private const string ScenePath = "Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity";
        private const int UdonSideWhite = GoGame.WHITE;
        private const int UdonSideBlack = GoGame.BLACK;

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool initialized;
        private static bool awaitingExternalMove;
        private static int udonSide;
        private static int kataSide;
        private static int udonVisits;
        private static int kataVisits;
        private static int lastMoveCount;
        private static int externalMoveIssuedAtMoveCount;
        private static string matchId;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour controllerProgram;
        private static UdonBehaviour inputProgram;
        private static UdonBehaviour settingsProgram;
        private static GoGame gameProxy;
        private static GoAiController controllerProxy;
        private static Process kataGo;
        private static StreamWriter artifact;

        [MenuItem("Tools/Pure Udon Go/Elo Pilot Udon 8 vs KataGo 20")]
        public static void VerifyUdon8VsKataGo20()
        {
            StartPilot(8, 20, UdonSideWhite);
        }

        [MenuItem("Tools/Pure Udon Go/Elo Pilot Udon 20 vs KataGo 20")]
        public static void VerifyUdon20VsKataGo20()
        {
            StartPilot(20, 20, UdonSideWhite);
        }

        [MenuItem("Tools/Pure Udon Go/Elo Pilot Udon 48 vs KataGo 20")]
        public static void VerifyUdon48VsKataGo20()
        {
            StartPilot(48, 20, UdonSideWhite);
        }

        private static void StartPilot(int requestedUdonVisits, int requestedKataVisits,
            int requestedUdonSide)
        {
            ResetRunner();
            udonVisits = Mathf.Clamp(requestedUdonVisits, 1, GoMctsSearch.MAX_SUPPORTED_VISITS);
            kataVisits = Mathf.Clamp(requestedKataVisits, 1, GoMctsSearch.MAX_SUPPORTED_VISITS);
            udonSide = requestedUdonSide == GoGame.BLACK ? GoGame.BLACK : GoGame.WHITE;
            kataSide = -udonSide;
            matchId = "udon" + udonVisits + "-katago" + kataVisits + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Generated Go scene was not loaded: " + ScenePath);

            GoBoardPool pool = UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if (pool != null && pool.tableRoots != null)
            {
                for (int i = 1; i < pool.tableRoots.Length; i++)
                {
                    if (pool.tableRoots[i] != null)
                        pool.tableRoots[i].SetActive(false);
                }
            }

            gameProxy = pool != null && pool.tableRoots != null && pool.tableRoots.Length > 0 &&
                pool.tableRoots[0] != null
                ? pool.tableRoots[0].GetComponentInChildren<GoGame>(true)
                : UnityEngine.Object.FindObjectOfType<GoGame>();
            controllerProxy = gameProxy == null ? null : gameProxy.GetComponentInChildren<GoAiController>(true);
            GoBoardInput inputProxy = gameProxy == null ? null : gameProxy.GetComponentInChildren<GoBoardInput>(true);
            if (gameProxy == null || controllerProxy == null || inputProxy == null || gameProxy.aiSettings == null)
                throw new InvalidOperationException("Production table is missing GoGame/controller/input/settings references");

            gameProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy);
            controllerProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(controllerProxy);
            inputProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(inputProxy);
            settingsProgram = UdonSharpEditorUtility.GetBackingUdonBehaviour(gameProxy.aiSettings);

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

            string artifactDirectory = Path.Combine(Application.dataPath, "..", OutputFolder);
            Directory.CreateDirectory(artifactDirectory);
            string artifactPath = Path.Combine(artifactDirectory, matchId + ".jsonl");
            artifact = new StreamWriter(artifactPath, false);
            artifact.AutoFlush = true;
            artifact.WriteLine("{\"schema\":\"pure-udon-go.elo-pilot.v1\",\"matchId\":\"" + matchId +
                "\",\"udonVisits\":" + udonVisits + ",\"kataGoVisits\":" + kataVisits +
                ",\"udonSide\":\"" + (udonSide == GoGame.BLACK ? "B" : "W") + "\"}");

            SessionState.SetBool("PureUdonGo.EloPilot.Pending", true);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            enteredPlayMode = false;
            initialized = false;
            awaitingExternalMove = false;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_ELO_PILOT_START match=" + matchId +
                " udonVisits=" + udonVisits + " kataGoVisits=" + kataVisits +
                " kataGoSide=" + (kataSide == GoGame.BLACK ? "B" : "W") +
                " timeoutSeconds=" + TimeoutSeconds);
            EditorSceneManager.SetActiveScene(scene);
            EditorApplication.isPlaying = true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            StopKataGo();
            if (artifact != null)
            {
                artifact.Dispose();
                artifact = null;
            }
            deadline = 0.0;
            enteredPlayMode = false;
            resultWritten = false;
            initialized = false;
            awaitingExternalMove = false;
            gameProgram = null;
            controllerProgram = null;
            inputProgram = null;
            settingsProgram = null;
            gameProxy = null;
            controllerProxy = null;
            lastMoveCount = 0;
            externalMoveIssuedAtMoveCount = -1;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode = true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_ELO_PILOT_PLAYMODE_ENTERED");
            }
            else if (state == PlayModeStateChange.EnteredEditMode &&
                SessionState.GetBool("PureUdonGo.EloPilot.Pending", false))
            {
                Fail("PlayMode ended before Elo pilot completed");
            }
        }

        private static void Pump()
        {
            if (resultWritten || !EditorApplication.isPlaying)
                return;
            if (!enteredPlayMode)
                enteredPlayMode = true;
            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                if (!initialized)
                {
                    ConfigureProductionTable();
                    initialized = true;
                    lastMoveCount = ReadInt(gameProgram, "moveCount");
                    StartKataGo();
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_ELO_PILOT_CONFIGURED moveCount=" + lastMoveCount);
                }

                int gameState = ReadInt(gameProgram, "gameState");
                if (gameState != GoGame.STATE_PLAYING)
                {
                    FinishGame(gameState);
                    return;
                }
                if (ReadInt(controllerProgram, "controllerState") == GoAiController.STATE_ERROR)
                {
                    Fail("Udon controller error: " + ReadString(controllerProgram, "lastError"));
                    return;
                }

                int moveCount = ReadInt(gameProgram, "moveCount");
                if (moveCount != lastMoveCount)
                {
                    int movedSide = ReadInt(gameProgram, "sideToMove") == GoGame.BLACK ? GoGame.WHITE : GoGame.BLACK;
                    int lastMove = ReadInt(gameProgram, "lastMove");
                    artifact?.WriteLine("{\"event\":\"move\",\"moveCount\":" + moveCount +
                        ",\"side\":\"" + (movedSide == GoGame.BLACK ? "B" : "W") +
                        "\",\"location\":" + lastMove + "}");
                    lastMoveCount = moveCount;
                    awaitingExternalMove = false;
                    if (movedSide == udonSide)
                        PlayKataGoMove();
                }

                if (moveCount == 0 && ReadInt(gameProgram, "sideToMove") == kataSide && !awaitingExternalMove)
                    PlayKataGoMove();
                FailIfTimedOut("Elo pilot timed out");
            }
            catch (Exception exception)
            {
                Fail("Elo pilot exception: " + exception);
            }
        }

        private static void ConfigureProductionTable()
        {
            controllerProgram.SetProgramVariable("autoStart", true);
            // Use the real public mode transitions.  SwitchAdaptiveToAI gives
            // White AI/Black human; swapping once gives Black AI/White human.
            SendEvent(gameProgram, "SwitchAdaptiveToAI");
            if (udonSide == GoGame.BLACK)
                SendEvent(gameProgram, "SwapHumanSideAndReset");

            settingsProgram.SetProgramVariable("sharedPreset", GoDifficultyProfile.BEGINNER);
            settingsProgram.SetProgramVariable("blackUseCustom", udonSide == GoGame.BLACK);
            settingsProgram.SetProgramVariable("whiteUseCustom", udonSide == GoGame.WHITE);
            settingsProgram.SetProgramVariable("blackCustomVisits", udonVisits);
            settingsProgram.SetProgramVariable("whiteCustomVisits", udonVisits);
            settingsProgram.SetProgramVariable("configRevision", ReadInt(settingsProgram, "configRevision") + 1);
            settingsProgram.SendCustomEvent("_onDeserialization");

            SendEvent(gameProgram, kataSide == GoGame.BLACK ? "ClaimBlack" : "ClaimWhite");
            SendEvent(gameProgram, "SetBlackStarts");
            SendEvent(gameProgram, "RequestStartMatch");
        }

        private static void StartKataGo()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Assets", "udon-go-ai"));
            string exe = Path.Combine(root, "Temp", "KaTrainParityTrace", "KataGo", "katago.exe");
            string config = Path.Combine(root, "Temp", "KaTrainParityTrace", "KataGo", "analysis_config.cfg");
            string model = Path.Combine(root, ".KataGO", "g170e-b10c128-s1141046784-d204142634.bin.gz");
            if (!File.Exists(exe) || !File.Exists(config) || !File.Exists(model))
                throw new InvalidOperationException("KataGo pilot inputs are missing");
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("analysis");
            info.ArgumentList.Add("-model");
            info.ArgumentList.Add(model);
            info.ArgumentList.Add("-config");
            info.ArgumentList.Add(config);
            info.ArgumentList.Add("-override-config");
            info.ArgumentList.Add("forDeterministicTesting=true,nnRandomize=false,numAnalysisThreads=1,numSearchThreads=1");
            kataGo = new Process { StartInfo = info };
            if (!kataGo.Start())
                throw new InvalidOperationException("KataGo process did not start");
            kataGo.StandardInput.AutoFlush = true;
        }

        private static void PlayKataGoMove()
        {
            if (awaitingExternalMove)
                return;
            string id = matchId + "-m" + lastMoveCount;
            List<string> moves = ReadMoveHistory();
            var query = new System.Text.StringBuilder();
            query.Append("{\"id\":\"").Append(id).Append("\",\"moves\":[");
            for (int i = 0; i < moves.Count; i++)
            {
                if (i > 0) query.Append(',');
                query.Append(moves[i]);
            }
            query.Append("],\"rules\":\"tromp-taylor\",\"komi\":7.5,\"boardXSize\":19,\"boardYSize\":19,");
            query.Append("\"maxVisits\":").Append(kataVisits).Append(",\"includePolicy\":true,\"includeOwnership\":false,\"analysisPVLen\":1}");
            kataGo.StandardInput.WriteLine(query.ToString());
            string response = ReadKataGoResponse(id);
            string move = ParseBestMove(response);
            if (string.IsNullOrWhiteSpace(move))
                throw new InvalidOperationException("KataGo returned no move for " + id);
            int location = ParseLocation(move);
            artifact?.WriteLine("{\"event\":\"kata_request\",\"id\":\"" + id +
                "\",\"move\":\"" + move + "\"}");
            if (location < 0)
                SendEvent(gameProgram, "Pass");
            else
                inputProgram.SendCustomEvent("Click" + location.ToString("D3"));
            awaitingExternalMove = true;
            externalMoveIssuedAtMoveCount = lastMoveCount;
        }

        private static List<string> ReadMoveHistory()
        {
            int count = Mathf.Clamp(ReadInt(gameProgram, "moveCount"), 0, GoGame.MAX_MOVES);
            int[] history = gameProgram.GetProgramVariable("moveHistory") as int[];
            if (history == null)
                throw new InvalidOperationException("GoGame.moveHistory backing array is unavailable");
            List<string> result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                int packed = history[i];
                int location = (packed & 511) - 2;
                bool white = (packed & 512) != 0;
                result.Add("[\"" + (white ? "W" : "B") + "\",\"" + FormatLocation(location) + "\"]");
            }
            return result;
        }

        private static string ReadKataGoResponse(string expectedId)
        {
            while (true)
            {
                string line = kataGo.StandardOutput.ReadLine();
                if (line == null)
                    throw new InvalidOperationException("KataGo closed stdout before response " + expectedId);
                if (line.IndexOf("\"id\":\"" + expectedId + "\"", StringComparison.Ordinal) >= 0)
                    return line;
            }
        }

        private static string ParseBestMove(string response)
        {
            int infos = response.IndexOf("\"moveInfos\":[", StringComparison.Ordinal);
            if (infos < 0) return "";
            int moveKey = response.IndexOf("\"move\":\"", infos, StringComparison.Ordinal);
            if (moveKey < 0) return "";
            int start = moveKey + 8;
            int end = response.IndexOf('"', start);
            return end > start ? response.Substring(start, end - start) : "";
        }

        private static string FormatLocation(int location)
        {
            if (location < 0) return "pass";
            const string columns = "ABCDEFGHJKLMNOPQRST";
            return columns[location % 19].ToString() + (19 - location / 19).ToString();
        }

        private static int ParseLocation(string value)
        {
            if (string.Equals(value, "pass", StringComparison.OrdinalIgnoreCase)) return -1;
            if (string.Equals(value, "resign", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("KataGo resigned; this pilot does not treat resignation as a board move");
            if (value.Length < 2) throw new InvalidOperationException("Invalid KataGo coordinate: " + value);
            const string columns = "ABCDEFGHJKLMNOPQRST";
            int column = columns.IndexOf(char.ToUpperInvariant(value[0]));
            int row = int.Parse(value.Substring(1));
            if (column < 0 || row < 1 || row > 19) throw new InvalidOperationException("Invalid KataGo coordinate: " + value);
            return (19 - row) * 19 + column;
        }

        private static void FinishGame(int gameState)
        {
            int winner = ReadInt(gameProgram, "winner");
            int moves = ReadInt(gameProgram, "moveCount");
            float score = ReadFloat(gameProgram, "finalWhiteMinusBlackScore");
            string winnerName = winner == GoGame.BLACK ? "B" : winner == GoGame.WHITE ? "W" : "D";
            bool udonWon = winner == udonSide;
            bool draw = winner == GoGame.EMPTY || gameState == GoGame.STATE_DRAW;
            if (draw) udonWon = false;
            artifact?.WriteLine("{\"event\":\"result\",\"gameState\":" + gameState +
                ",\"winner\":\"" + winnerName + "\",\"moves\":" + moves +
                ",\"scoreWhiteMinusBlack\":" + score.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"udonResult\":\"" + (draw ? "D" : udonWon ? "W" : "L") + "\"}");
            Debug.Log("PURE_UDON_GO_CLIENTSIM_ELO_PILOT_RESULT match=" + matchId +
                " udonResult=" + (draw ? "D" : udonWon ? "W" : "L") +
                " winner=" + winnerName + " moves=" + moves + " score=" + score);
            StopWithResult(true);
        }

        private static void StopKataGo()
        {
            if (kataGo == null) return;
            try
            {
                if (!kataGo.HasExited)
                {
                    kataGo.StandardInput.Close();
                    kataGo.WaitForExit(5000);
                }
            }
            catch { }
            kataGo.Dispose();
            kataGo = null;
        }

        private static void SendEvent(UdonBehaviour program, string eventName)
        {
            if (program == null) throw new InvalidOperationException("Missing Udon program for event " + eventName);
            program.SendCustomEvent(eventName);
        }

        private static int ReadInt(UdonBehaviour program, string variable)
        {
            object value = program.GetProgramVariable(variable);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static float ReadFloat(UdonBehaviour program, string variable)
        {
            object value = program.GetProgramVariable(variable);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static string ReadString(UdonBehaviour program, string variable)
        {
            object value = program.GetProgramVariable(variable);
            return value == null ? "" : value.ToString();
        }

        private static void FailIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup < deadline) return;
            Fail(message);
        }

        private static void Fail(string message)
        {
            if (resultWritten) return;
            resultWritten = true;
            artifact?.WriteLine("{\"event\":\"error\",\"message\":\"" + message.Replace("\"", "'") + "\"}");
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_ELO_PILOT_FAIL " + message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            if (!resultWritten) resultWritten = true;
            SessionState.SetBool("PureUdonGo.EloPilot.Pending", false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= Pump;
            StopKataGo();
            if (artifact != null)
            {
                artifact.Flush();
                artifact.Dispose();
                artifact = null;
            }
            Debug.Log("PURE_UDON_GO_CLIENTSIM_ELO_PILOT_DONE pass=" + pass);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
