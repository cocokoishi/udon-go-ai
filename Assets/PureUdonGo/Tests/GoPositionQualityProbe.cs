using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only position-quality probe. Fixed sequences are played by
    /// the real GoGame Udon program while the AI controller is paused; each
    /// target position is then searched by the real Udon GPU/PUCT path.
    /// </summary>
    public sealed class GoPositionQualityProbe : UdonSharpBehaviour
    {
        // Fixed search budget for the PUCT smoke corpus; this is intentionally
        // independent from the public Advanced preset value.
        public const int DIAGNOSTIC_VISITS = 32;
        public GoGame game;
        public GoAiController controller;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiSettings aiSettings;
        public bool probeFinished;
        public bool probePassed;
        public int scenarioIndex;
        public int[] selectedMoves = new int[4];
        public int[] policyMoves = new int[4];
        public int[] actualVisits = new int[4];
        public int[] moveCounts = new int[4];
        public int[] aiMoveDeltas = new int[4];
        public int[] treeNodes = new int[4];
        public int[] treeEdges = new int[4];
        public int[] readerStages = new int[4];
        public int[] ownershipReadbacks = new int[4];
        public string failure;

        public int[] scenario0Moves = { 60, 300 };
        public int[] scenario1Moves = { 1, 0, 19 };
        public int[] scenario2Moves = { 180, 181, 161, 199, 179, 201, 160, 220 };
        public int[] scenario3Moves = { 60, 300, 40, 320, 61, 299, 59, 301, 62, 298, 58 };

        private bool runningScenario;
        private int baselineMoveCount;
        private int baselineAiMoves;

        public void RunPositionQualityProbe()
        {
            probeFinished = false;
            probePassed = false;
            scenarioIndex = 0;
            failure = "";
            for (int i = 0; i < 4; i++)
            {
                selectedMoves[i] = GoGame.NONE;
                policyMoves[i] = GoGame.NONE;
                actualVisits[i] = 0;
                moveCounts[i] = 0;
                aiMoveDeltas[i] = 0;
                treeNodes[i] = 0;
                treeEdges[i] = 0;
                readerStages[i] = 0;
                ownershipReadbacks[i] = 0;
            }
            if (game == null || controller == null || blackDifficulty == null ||
                whiteDifficulty == null)
            {
                Finish("position-quality probe references are incomplete");
                return;
            }

            controller.autoStart = false;
            game.SetMatchMode(3);
            // Position-quality evidence uses the Advanced parameter base with
            // a fixed 32-visit Custom override; the generated default remains
            // Beginner/8.
            if(aiSettings!=null)
            {
                aiSettings.ApplyPackedVisits(GoDifficultyProfile.ADVANCED,
                    DIAGNOSTIC_VISITS, DIAGNOSTIC_VISITS);
            }
            else
            {
                blackDifficulty.ApplyResolvedLocal(GoDifficultyProfile.ADVANCED,DIAGNOSTIC_VISITS,0,true);
                whiteDifficulty.ApplyResolvedLocal(GoDifficultyProfile.ADVANCED,DIAGNOSTIC_VISITS,0,true);
            }
            BeginScenario();
        }

        public void Update()
        {
            if (probeFinished || !runningScenario)
                return;
            if (game == null || controller == null)
            {
                Finish("position-quality probe lost a runtime reference");
                return;
            }
            if (controller.controllerState == GoAiController.STATE_ERROR)
            {
                Finish("AI controller error: " + controller.lastError);
                return;
            }

            if (game.moveCount <= baselineMoveCount ||
                controller.aiMoves <= baselineAiMoves)
                return;

            selectedMoves[scenarioIndex] = game.lastMove;
            moveCounts[scenarioIndex] = game.moveCount;
            aiMoveDeltas[scenarioIndex] = controller.aiMoves - baselineAiMoves;
            if (controller.search != null)
            {
                policyMoves[scenarioIndex] = controller.search.GetRootPriorMove();
                actualVisits[scenarioIndex] = controller.search.visitsCompleted;
                treeNodes[scenarioIndex] = controller.search.treeNodeCount;
                treeEdges[scenarioIndex] = controller.search.treeEdgeCount;
            }
            if (controller.reader != null)
            {
                readerStages[scenarioIndex] = controller.reader.completedStages;
                ownershipReadbacks[scenarioIndex] = controller.reader.ownershipReadbackCount;
            }
            runningScenario = false;
            controller.autoStart = false;

            if (scenarioIndex >= 3)
            {
                probePassed = true;
                probeFinished = true;
                return;
            }
            scenarioIndex++;
            BeginScenario();
        }

        private void BeginScenario()
        {
            if (probeFinished)
                return;
            controller.InvalidateSearch();
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            if (!PlayScenarioMoves(scenarioIndex))
                return;
            baselineMoveCount = game.moveCount;
            baselineAiMoves = controller.aiMoves;
            runningScenario = true;
            controller.autoStart = true;
        }

        private bool PlayScenarioMoves(int index)
        {
            int[] moves = index == 0 ? scenario0Moves :
                index == 1 ? scenario1Moves :
                index == 2 ? scenario2Moves : scenario3Moves;
            if (moves == null || moves.Length < 1)
            {
                Finish("scenario has no fixed moves: " + index);
                return false;
            }
            for (int i = 0; i < moves.Length; i++)
            {
                if (!game.TryPlayFromAI(moves[i]))
                {
                    Finish("fixed sequence illegal scenario=" + index +
                        " index=" + i + " location=" + moves[i]);
                    return false;
                }
            }
            return true;
        }

        private void Finish(string message)
        {
            failure = message;
            runningScenario = false;
            probeFinished = true;
        }
    }
}
