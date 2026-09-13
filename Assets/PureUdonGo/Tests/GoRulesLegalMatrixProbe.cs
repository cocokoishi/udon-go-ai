using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only legal-move matrix producer. It advances the production
    /// GoGame through one fixed history and, at eleven checkpoints, asks the
    /// real Udon rules API about every one of the 361 intersections plus PASS.
    /// The editor verifier exports the observations; an independent Python
    /// reference owns the expected masks.
    /// </summary>
    public sealed class GoRulesLegalMatrixProbe : UdonSharpBehaviour
    {
        public const int ACTION_COUNT = 160;
        public const int POSITION_COUNT = 11;

        public GoGame game;
        public int[] checkpoints = { 0, 1, 2, 4, 8, 16, 32, 64, 96, 128, 160 };
        public int[] moves =
        {
            60,300,72,288,40,320,54,59,61,299,91,269,43,317,111,235,148,119,
            354,7,79,173,181,197,223,105,90,208,175,2,115,65,304,164,36,254,
            152,53,129,338,117,76,312,201,167,240,191,337,135,67,-1,74,262,
            276,114,315,17,44,37,270,333,16,274,62,3,318,243,204,136,131,190,
            149,237,143,87,134,196,71,293,278,125,69,47,242,29,42,228,279,108,
            33,216,218,6,339,-1,277,324,257,230,24,100,153,336,210,101,249,170,
            305,310,80,99,326,217,144,81,195,241,22,261,158,49,1,275,226,244,
            86,316,325,107,301,247,98,0,329,172,19,342,214,-1,185,307,189,97,
            13,349,48,106,4,199,84,347,166,295,133,219,130,255,171,357,121
        };

        public bool probeFinished;
        public bool probePassed;
        public bool running;
        public int actionIndex;
        public int checkpointIndex;
        public int acceptedActionCount;
        public int finalMoveCount;
        public int finalSideToMove;
        public int finalGameState;
        public int finalPositionHistoryCount;
        public string failure = "";

        public bool[] accepted = new bool[ACTION_COUNT];
        public int[] moveCounts = new int[ACTION_COUNT];
        public int[] sideToMove = new int[ACTION_COUNT];
        public int[] gameStates = new int[ACTION_COUNT];
        public int[] positionHistoryCounts = new int[ACTION_COUNT];
        public int[] boardTrace = new int[POSITION_COUNT * GoGame.AREA];
        public bool[] passLegalTrace = new bool[POSITION_COUNT];
        public bool[] legalTrace = new bool[POSITION_COUNT * GoGame.AREA];
        public int[] legalCounts = new int[POSITION_COUNT];

        public void RunRulesLegalMatrixProbe()
        {
            probeFinished = false;
            probePassed = false;
            running = false;
            actionIndex = 0;
            checkpointIndex = 0;
            acceptedActionCount = 0;
            finalMoveCount = 0;
            finalSideToMove = GoGame.EMPTY;
            finalGameState = GoGame.STATE_PLAYING;
            finalPositionHistoryCount = 0;
            failure = "";
            ClearOutputs();
            if (game == null || moves == null || moves.Length != ACTION_COUNT ||
                checkpoints == null || checkpoints.Length != POSITION_COUNT)
            {
                Finish("legal matrix probe references or corpus are incomplete");
                return;
            }

            game.SetMatchMode(3);
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            running = true;
        }

        public void Update()
        {
            if (!running || probeFinished || game == null)
                return;

            if (checkpointIndex >= POSITION_COUNT)
            {
                FinishRun();
                return;
            }

            int targetAction = checkpoints[checkpointIndex];
            if (targetAction < 0 || targetAction > ACTION_COUNT ||
                targetAction < actionIndex)
            {
                Finish("checkpoint order is invalid at index=" + checkpointIndex);
                return;
            }

            if (actionIndex < targetAction)
            {
                int location = moves[actionIndex];
                int beforeMoveCount = game.moveCount;
                bool actionAccepted;
                if (location == GoGame.PASS)
                {
                    game.PassFromAI();
                    actionAccepted = game.moveCount == beforeMoveCount + 1;
                }
                else
                {
                    actionAccepted = game.TryPlayFromAI(location);
                }
                if (!actionAccepted)
                {
                    Finish("legal matrix corpus action rejected index=" + actionIndex +
                        " location=" + location);
                    return;
                }
                accepted[actionIndex] = true;
                moveCounts[actionIndex] = game.moveCount;
                sideToMove[actionIndex] = game.sideToMove;
                gameStates[actionIndex] = game.gameState;
                positionHistoryCounts[actionIndex] = game.positionHistoryCount;
                acceptedActionCount++;
                actionIndex++;
                return;
            }

            CaptureMatrix(checkpointIndex);
            checkpointIndex++;
        }

        private void CaptureMatrix(int index)
        {
            int offset = index * GoGame.AREA;
            int count = 0;
            for (int location = 0; location < GoGame.AREA; location++)
            {
                boardTrace[offset + location] = game.board[location];
                bool legal = game.IsRulesLegalMove(location);
                legalTrace[offset + location] = legal;
                if (legal)
                    count++;
            }
            passLegalTrace[index] = game.IsRulesLegalMove(GoGame.PASS);
            legalCounts[index] = count;
        }

        private void FinishRun()
        {
            finalMoveCount = game.moveCount;
            finalSideToMove = game.sideToMove;
            finalGameState = game.gameState;
            finalPositionHistoryCount = game.positionHistoryCount;
            probePassed = acceptedActionCount == ACTION_COUNT &&
                actionIndex == ACTION_COUNT && checkpointIndex == POSITION_COUNT;
            if (!probePassed)
                failure = "legal matrix run did not reach every checkpoint";
            running = false;
            probeFinished = true;
        }

        private void ClearOutputs()
        {
            for (int i = 0; i < ACTION_COUNT; i++)
            {
                accepted[i] = false;
                moveCounts[i] = 0;
                sideToMove[i] = GoGame.EMPTY;
                gameStates[i] = GoGame.STATE_PLAYING;
                positionHistoryCounts[i] = 0;
            }
            for (int i = 0; i < POSITION_COUNT; i++)
            {
                passLegalTrace[i] = false;
                legalCounts[i] = 0;
            }
            for (int i = 0; i < POSITION_COUNT * GoGame.AREA; i++)
            {
                boardTrace[i] = GoGame.EMPTY;
                legalTrace[i] = false;
            }
        }

        private void Finish(string message)
        {
            running = false;
            failure = message;
            probeFinished = true;
        }
    }
}
