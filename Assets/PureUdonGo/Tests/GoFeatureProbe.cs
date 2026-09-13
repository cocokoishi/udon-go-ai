using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only feature export probe. Each state is constructed by the
    /// real GoGame Udon methods, then encoded by the production Udon feature
    /// encoder into the public arrays for an independent KataGo comparison.
    /// </summary>
    public sealed class GoFeatureProbe : UdonSharpBehaviour
    {
        public const int CASE_COUNT = 16;
        private const int RANDOM_START = 8;

        public GoGame game;
        public GoFeatureEncoder encoder;
        public GoAiController controller;
        public bool probeFinished;
        public bool probePassed;
        public bool handicapSelfKomiPassed;
        public bool running;
        public int scenarioIndex;
        public int scenarioStage;
        public int moveIndex;
        public int copyCursor;
        public int[] currentMoves;
        public bool[] encoded = new bool[CASE_COUNT];
        public int[] moveCounts = new int[CASE_COUNT];
        public int[] sideToMove = new int[CASE_COUNT];
        public int[] gameStates = new int[CASE_COUNT];
        public int[] consecutivePasses = new int[CASE_COUNT];
        public int[] koLocations = new int[CASE_COUNT];
        public float[] spatial = new float[CASE_COUNT * GoFeatureEncoder.SPATIAL_COUNT];
        public float[] global = new float[CASE_COUNT * GoFeatureEncoder.GLOBAL_CHANNELS];
        public int firstEncodeSpatialClearCount;
        public int firstEncodeSpatialClearSlices;
        public bool firstEncodeUsedFreshOwnedSpatial;
        public string failure;
        private bool handicapSelfKomiStarted;

        public int[] emptyMoves = new int[0];
        public int[] openingMoves = { 60, 300, 72, 288, 40, 320, 54 };
        public int[] fightMoves = { 60, 300, 72, 288, 40, 320, 54, 59, 61, 299, 91, 269, 43, 317, 111 };
        public int[] ladderMoves = { 60, 300, 72, 288, 40, 320, 54, 59, 61, 299, 91, 269, 43, 317, 111, 63, 297, 129, 231 };
        public int[] captureMoves = { 288, 287, 18, 307, 306, 360, 268, 342, 286 };
        public int[] passHistoryMoves = { 60, GoGame.PASS };
        public int[] doublePassMoves = { GoGame.PASS, GoGame.PASS };
        public int[] emptyChineseMoves = new int[0];
        public int[] randomSeeds = { 17391, 48127, 90211, 137821, 22117, 66301, 104729, 191939 };
        public int[] randomTargetMoves = { 32, 64, 96, 144, 32, 64, 96, 144 };
        public bool[] randomIncludePasses = { false, true, false, true, false, true, false, true };
        public int[] randomMoves0 = new int[144];
        public int[] randomMoves1 = new int[144];
        public int[] randomMoves2 = new int[144];
        public int[] randomMoves3 = new int[144];
        public int[] randomMoves4 = new int[144];
        public int[] randomMoves5 = new int[144];
        public int[] randomMoves6 = new int[144];
        public int[] randomMoves7 = new int[144];

        private int randomState;
        private bool randomPreviousWasPass;

        public void RunFeatureProbe()
        {
            probeFinished = false;
            probePassed = false;
            handicapSelfKomiPassed = false;
            handicapSelfKomiStarted = false;
            running = false;
            scenarioIndex = 0;
            scenarioStage = 0;
            moveIndex = 0;
            copyCursor = 0;
            firstEncodeSpatialClearCount = -1;
            firstEncodeSpatialClearSlices = -1;
            firstEncodeUsedFreshOwnedSpatial = false;
            currentMoves = null;
            failure = "";
            for (int i = 0; i < CASE_COUNT; i++)
            {
                encoded[i] = false;
                moveCounts[i] = 0;
                sideToMove[i] = GoGame.EMPTY;
                gameStates[i] = GoGame.STATE_PLAYING;
                consecutivePasses[i] = 0;
                koLocations[i] = GoGame.NONE;
            }
            if (game == null || encoder == null || controller == null)
            {
                Finish("feature probe references are incomplete");
                return;
            }

            controller.autoStart = false;
            game.SetMatchMode(3);
            running = true;
        }

        // ClientSim/Udon enforces a roughly ten-second VM budget per event.
        // Encode one complete position per frame so this real Udon probe does
        // not turn seven valid encodes into a monolithic timeout.
        public void Update()
        {
            if (!running || probeFinished)
                return;
            if (scenarioIndex >= CASE_COUNT)
            {
                if (!handicapSelfKomiStarted)
                {
                    game.RequestForceReset();
                    game.areaScoring = true;
                    game.komiTimes2 = 15;
                    if (!game.SetHandicap(5))
                    {
                        Finish("handicap self-komi setup failed");
                        return;
                    }
                    if (!encoder.BeginEncodeGame(game))
                    {
                        Finish("handicap self-komi encoder could not start: " +
                            encoder.lastError);
                        return;
                    }
                    handicapSelfKomiStarted = true;
                    scenarioStage = 4;
                    return;
                }
                if (scenarioStage == 4)
                {
                    int state = encoder.StepEncode(2);
                    if (state == GoFeatureEncoder.ENCODE_RUNNING)
                        return;
                    if (state != GoFeatureEncoder.ENCODE_COMPLETE)
                    {
                        Finish("handicap self-komi encoder failed: " + encoder.lastError);
                        return;
                    }
                    // Five fixed handicap stones imply 7.5 ordinary komi +
                    // 5.0 Chinese bonus = 12.5 points for White.  With White
                    // to move the V7 self-komi channel is +12.5 / 20.
                    handicapSelfKomiPassed = encoder.globalOutput[5] > 0.6249f &&
                        encoder.globalOutput[5] < 0.6251f;
                    if (!handicapSelfKomiPassed)
                        failure = "handicap self-komi global[5]=" +
                            encoder.globalOutput[5];
                    running = false;
                    probePassed = handicapSelfKomiPassed;
                    probeFinished = true;
                    return;
                }
                running = false;
                probePassed = true;
                probeFinished = true;
                return;
            }

            if (scenarioStage == 0)
            {
                bool chinese = IsChineseScenario(scenarioIndex);
                game.positionalSuperko = !chinese;
                game.multiStoneSuicideLegal = !chinese;
                game.areaScoring = true;
                game.RequestForceReset();
                game.SetBlackStarts();
                game.RequestStartMatch();
                if (IsRandomScenario(scenarioIndex))
                {
                    currentMoves = RandomMovesFor(scenarioIndex - RANDOM_START);
                    randomState = randomSeeds[scenarioIndex - RANDOM_START];
                    randomPreviousWasPass = false;
                }
                else
                    currentMoves = MovesFor(scenarioIndex);
                if (currentMoves == null)
                {
                    Finish("scenario move array is null");
                    return;
                }
                moveIndex = 0;
                scenarioStage = 1;
                return;
            }

            if (scenarioStage == 1)
            {
                int targetMoves = IsRandomScenario(scenarioIndex)
                    ? randomTargetMoves[scenarioIndex - RANDOM_START]
                    : currentMoves.Length;
                if (moveIndex < targetMoves)
                {
                    if (IsRandomScenario(scenarioIndex))
                    {
                        if (!GenerateRandomMove(moveIndex))
                        {
                            Finish("random scenario move generation exhausted case=" +
                                scenarioIndex + " index=" + moveIndex);
                            return;
                        }
                    }
                    else
                    {
                        int location = currentMoves[moveIndex];
                        bool accepted;
                        if (location == GoGame.PASS)
                        {
                            int before = game.moveCount;
                            game.PassFromAI();
                            accepted = game.moveCount == before + 1;
                        }
                        else
                            accepted = game.TryPlayFromAI(location);
                        if (!accepted)
                        {
                            Finish("fixed scenario move rejected index=" + moveIndex +
                                " location=" + location);
                            return;
                        }
                    }
                    moveIndex++;
                    return;
                }

                if (!encoder.BeginEncodeGame(game))
                {
                    Finish("encoder could not start case=" + scenarioIndex +
                        ": " + encoder.lastError);
                    return;
                }
                scenarioStage = 2;
                return;
            }

            if (scenarioStage == 2)
            {
                int state = encoder.StepEncode(2);
                if (state == GoFeatureEncoder.ENCODE_RUNNING)
                    return;
                if (state != GoFeatureEncoder.ENCODE_COMPLETE)
                {
                    Finish("encoder error case=" + scenarioIndex +
                        ": " + encoder.lastError);
                    return;
                }
                if (scenarioIndex == 0)
                {
                    firstEncodeSpatialClearCount = encoder.lastSpatialClearCount;
                    firstEncodeSpatialClearSlices = encoder.lastSpatialClearSlices;
                    firstEncodeUsedFreshOwnedSpatial = encoder.lastEncodeUsedFreshOwnedSpatial;
                }
                int index = scenarioIndex;
                moveCounts[index] = game.moveCount;
                sideToMove[index] = game.sideToMove;
                gameStates[index] = game.gameState;
                consecutivePasses[index] = game.consecutivePasses;
                koLocations[index] = game.koLoc;
                copyCursor = 0;
                scenarioStage = 3;
                return;
            }

            if (scenarioStage == 3)
            {
                int index = scenarioIndex;
                int spatialOffset = index * GoFeatureEncoder.SPATIAL_COUNT;
                int end = copyCursor + 512;
                if (end > GoFeatureEncoder.SPATIAL_COUNT)
                    end = GoFeatureEncoder.SPATIAL_COUNT;
                while (copyCursor < end)
                {
                    spatial[spatialOffset + copyCursor] = encoder.spatialOutput[copyCursor];
                    copyCursor++;
                }
                if (copyCursor < GoFeatureEncoder.SPATIAL_COUNT)
                    return;
                int globalOffset = index * GoFeatureEncoder.GLOBAL_CHANNELS;
                for (int i = 0; i < GoFeatureEncoder.GLOBAL_CHANNELS; i++)
                    global[globalOffset + i] = encoder.globalOutput[i];
                encoded[index] = true;
                scenarioIndex++;
                scenarioStage = 0;
                if (scenarioIndex >= CASE_COUNT)
                {
                    running = false;
                    probePassed = true;
                    probeFinished = true;
                }
            }
        }

        private int[] MovesFor(int index)
        {
            if (index == 0) return emptyMoves;
            if (index == 1) return openingMoves;
            if (index == 2) return fightMoves;
            if (index == 3) return ladderMoves;
            if (index == 4) return captureMoves;
            if (index == 5) return passHistoryMoves;
            if (index == 6) return doublePassMoves;
            if (index == 7) return emptyChineseMoves;
            return null;
        }

        private bool IsRandomScenario(int index)
        {
            return index >= RANDOM_START && index < CASE_COUNT;
        }

        private bool IsChineseScenario(int index)
        {
            return index == 7 || index >= 12;
        }

        private int[] RandomMovesFor(int index)
        {
            if (index == 0) return randomMoves0;
            if (index == 1) return randomMoves1;
            if (index == 2) return randomMoves2;
            if (index == 3) return randomMoves3;
            if (index == 4) return randomMoves4;
            if (index == 5) return randomMoves5;
            if (index == 6) return randomMoves6;
            return randomMoves7;
        }

        private bool GenerateRandomMove(int index)
        {
            int randomIndex = scenarioIndex - RANDOM_START;
            bool includePasses = randomIncludePasses[randomIndex];
            if (includePasses && !randomPreviousWasPass && NextRandom() % 19 == 0)
            {
                int before = game.moveCount;
                game.PassFromAI();
                if (game.moveCount == before + 1 &&
                    game.gameState == GoGame.STATE_PLAYING)
                {
                    currentMoves[index] = GoGame.PASS;
                    randomPreviousWasPass = true;
                    return true;
                }
            }

            for (int attempt = 0; attempt < GoGame.AREA * 2; attempt++)
            {
                int location = NextRandom() % GoGame.AREA;
                if (!game.TryPlayFromAI(location))
                    continue;
                currentMoves[index] = location;
                randomPreviousWasPass = false;
                return true;
            }
            return false;
        }

        private int NextRandom()
        {
            randomState = randomState * 1103515245 + 12345;
            return randomState & 0x7fffffff;
        }

        private void Finish(string message)
        {
            running = false;
            failure = message;
            probeFinished = true;
        }
    }
}
