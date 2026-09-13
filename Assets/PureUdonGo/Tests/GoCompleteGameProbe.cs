using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only complete-game pipeline smoke probe. Both sides use the real GoAiController;
    /// the custom one-visit profile keeps the test finite while still going
    /// through Udon MCTS, shader inference, async readback and GoGame commit.
    /// This probe is attached only to an unsaved test scene by its editor
    /// verifier and is never wired into the production world.
    /// </summary>
    public sealed class GoCompleteGameProbe : UdonSharpBehaviour
    {
        public GoGame game;
        public GoAiController controller;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiSettings aiSettings;
        public bool started;
        public bool probeFinished;
        public bool probePassed;
        public int moveCountAtFinish;
        public int aiMovesAtFinish;
        public int passMoves;
        public int terminalState;
        public int winner;
        public int blackArea;
        public int whiteArea;
        public float score;
        public string failure;
        public int targetVisits=1;
        // -1 keeps the probe on its finite custom profile.  Editor
        // performance verifiers use the public preset constants so their
        // samples exercise the same visit and transition budgets exposed in
        // the generated world.
        public int profilePreset=-1;

        private int lastMoveCount;

        public void RunCompleteGame()
        {
            started = false;
            probeFinished = false;
            probePassed = false;
            moveCountAtFinish = 0;
            aiMovesAtFinish = 0;
            passMoves = 0;
            terminalState = GoGame.STATE_PLAYING;
            winner = GoGame.EMPTY;
            blackArea = 0;
            whiteArea = 0;
            score = 0f;
            failure = "";
            if (game == null || controller == null || blackDifficulty == null ||
                whiteDifficulty == null)
            {
                Finish("complete-game probe references are incomplete");
                return;
            }

            controller.autoStart = true;
            game.SetMatchMode(3);
            int visits=targetVisits<1?1:targetVisits;
            GoAiSettings settings=aiSettings==null?game.aiSettings:aiSettings;
            if(settings!=null)
            {
                int shared=profilePreset>=GoDifficultyProfile.BEGINNER&&
                    profilePreset<=GoDifficultyProfile.ULTRAHARD
                    ?profilePreset:GoDifficultyProfile.BEGINNER;
                bool custom=profilePreset<GoDifficultyProfile.BEGINNER||
                    profilePreset>GoDifficultyProfile.ULTRAHARD;
                int packedVisits=custom?visits:-visits;
                settings.ApplyPackedVisits(shared,packedVisits,packedVisits);
            }
            else
            {
                blackDifficulty.ApplyResolvedLocal(GoDifficultyProfile.BEGINNER,visits,0,true);
                whiteDifficulty.ApplyResolvedLocal(GoDifficultyProfile.BEGINNER,visits,0,true);
            }
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            lastMoveCount = game.moveCount;
            started = true;
        }

        public void Update()
        {
            if (!started || probeFinished)
                return;
            if (game == null || controller == null)
            {
                Finish("complete-game probe lost a runtime reference");
                return;
            }

            if (game.moveCount != lastMoveCount)
            {
                if (game.lastMove == GoGame.PASS)
                    passMoves++;
                lastMoveCount = game.moveCount;
            }

            if (game.gameState != GoGame.STATE_PLAYING)
            {
                moveCountAtFinish = game.moveCount;
                aiMovesAtFinish = controller.aiMoves;
                terminalState = game.gameState;
                winner = game.winner;
                blackArea = game.finalBlackArea;
                whiteArea = game.finalWhiteArea;
                score = game.finalWhiteMinusBlackScore;
                if (moveCountAtFinish < 3)
                    Finish("terminal game was too short: " + moveCountAtFinish);
                else if (passMoves < 2)
                    Finish("terminal game did not reach two passes: " + passMoves);
                else
                {
                    probePassed = true;
                    probeFinished = true;
                }
                return;
            }

            if (game.moveCount >= GoGame.MAX_MOVES)
                Finish("game reached MAX_MOVES without terminal scoring");
        }

        private void Finish(string message)
        {
            failure = message;
            moveCountAtFinish = game == null ? 0 : game.moveCount;
            aiMovesAtFinish = controller == null ? 0 : controller.aiMoves;
            terminalState = game == null ? GoGame.STATE_PLAYING : game.gameState;
            winner = game == null ? GoGame.EMPTY : game.winner;
            probeFinished = true;
        }
    }
}
