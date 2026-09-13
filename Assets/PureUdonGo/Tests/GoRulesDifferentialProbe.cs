using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only trace producer for a fixed long Go history. The probe
    /// performs one real GoGame Udon action per frame and records the full
    /// board plus the two history planes after every accepted action. An
    /// independent Python reference consumes the trace; this class does not
    /// reimplement or self-approve the expected results.
    /// </summary>
    public sealed class GoRulesDifferentialProbe : UdonSharpBehaviour
    {
        public const int MOVE_COUNT = 160;

        public GoGame game;
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
        public int moveIndex;
        public int acceptedMoveCount;
        public int finalMoveCount;
        public int finalSideToMove;
        public int finalConsecutivePasses;
        public int finalBlackCaptures;
        public int finalWhiteCaptures;
        public int finalKoLoc;
        public int finalGameState;
        public int finalPositionHistoryCount;
        public string failure = "";

        public bool[] accepted = new bool[MOVE_COUNT];
        public int[] moveCounts = new int[MOVE_COUNT];
        public int[] sideToMove = new int[MOVE_COUNT];
        public int[] consecutivePasses = new int[MOVE_COUNT];
        public int[] blackCaptures = new int[MOVE_COUNT];
        public int[] whiteCaptures = new int[MOVE_COUNT];
        public int[] koLocations = new int[MOVE_COUNT];
        public int[] gameStates = new int[MOVE_COUNT];
        public int[] positionHistoryCounts = new int[MOVE_COUNT];
        public int[] lastMoves = new int[MOVE_COUNT];
        public int[] boardTrace = new int[MOVE_COUNT * GoGame.AREA];
        public int[] previousBoard1Trace = new int[MOVE_COUNT * GoGame.AREA];
        public int[] previousBoard2Trace = new int[MOVE_COUNT * GoGame.AREA];

        public void RunRulesDifferentialProbe()
        {
            probeFinished = false;
            probePassed = false;
            running = false;
            moveIndex = 0;
            acceptedMoveCount = 0;
            finalMoveCount = 0;
            finalSideToMove = GoGame.EMPTY;
            finalConsecutivePasses = 0;
            finalBlackCaptures = 0;
            finalWhiteCaptures = 0;
            finalKoLoc = GoGame.NONE;
            finalGameState = GoGame.STATE_PLAYING;
            finalPositionHistoryCount = 0;
            failure = "";
            for (int i = 0; i < MOVE_COUNT; i++)
            {
                accepted[i] = false;
                moveCounts[i] = 0;
                sideToMove[i] = GoGame.EMPTY;
                consecutivePasses[i] = 0;
                blackCaptures[i] = 0;
                whiteCaptures[i] = 0;
                koLocations[i] = GoGame.NONE;
                gameStates[i] = GoGame.STATE_PLAYING;
                positionHistoryCounts[i] = 0;
                lastMoves[i] = GoGame.NONE;
            }
            if (game == null || moves == null || moves.Length != MOVE_COUNT)
            {
                Finish("rules differential probe references or sequence are incomplete");
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
            if (moveIndex >= MOVE_COUNT)
            {
                finalMoveCount = game.moveCount;
                finalSideToMove = game.sideToMove;
                finalConsecutivePasses = game.consecutivePasses;
                finalBlackCaptures = game.blackCaptures;
                finalWhiteCaptures = game.whiteCaptures;
                finalKoLoc = game.koLoc;
                finalGameState = game.gameState;
                finalPositionHistoryCount = game.positionHistoryCount;
                running = false;
                probePassed = acceptedMoveCount == MOVE_COUNT;
                if (!probePassed)
                    failure = "not every fixed action was accepted";
                probeFinished = true;
                return;
            }

            int index = moveIndex;
            int location = moves[index];
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
                Finish("fixed action rejected index=" + index + " location=" + location);
                return;
            }

            accepted[index] = true;
            acceptedMoveCount++;
            moveCounts[index] = game.moveCount;
            sideToMove[index] = game.sideToMove;
            consecutivePasses[index] = game.consecutivePasses;
            blackCaptures[index] = game.blackCaptures;
            whiteCaptures[index] = game.whiteCaptures;
            koLocations[index] = game.koLoc;
            gameStates[index] = game.gameState;
            positionHistoryCounts[index] = game.positionHistoryCount;
            lastMoves[index] = game.lastMove;
            int offset = index * GoGame.AREA;
            for (int i = 0; i < GoGame.AREA; i++)
            {
                boardTrace[offset + i] = game.board[i];
                previousBoard1Trace[offset + i] = game.previousBoard1[i];
                previousBoard2Trace[offset + i] = game.previousBoard2[i];
            }
            moveIndex++;
        }

        private void Finish(string message)
        {
            running = false;
            failure = message;
            probeFinished = true;
        }
    }
}
