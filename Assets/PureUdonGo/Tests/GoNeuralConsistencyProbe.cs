using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only neural output export probe. Each fixed position is built
    /// by the real synchronized GoGame Udon program, then the real cooperative
    /// controller runs one complete diagnostic PUCT search using the Advanced
    /// parameter base. The controller keeps
    /// the first root readback, and this probe copies that snapshot for a
    /// separate numeric-oracle tool. This probe itself does not compare values.
    /// </summary>
    public sealed class GoNeuralConsistencyProbe : UdonSharpBehaviour
    {
        public const int CASE_COUNT = 8;
        public const int POLICY_COUNT = GoGame.AREA;
        // This is a fixed numerical-differential budget, not the public
        // Advanced preset. Keep it stable when product presets change.
        public const int DIAGNOSTIC_VISITS = 32;

        public GoGame game;
        public GoAiController controller;
        public GoFeatureEncoder encoder;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiSettings aiSettings;

        public bool probeFinished;
        public bool probePassed;
        public int scenarioIndex;
        public bool diagnosticRunningScenario;
        public bool diagnosticEncodingRoot;
        public int diagnosticEncodeState;
        public int diagnosticEncodeStage;
        public int diagnosticControllerState;
        public int diagnosticSearchPhase;
        public int diagnosticGameMoveCount;
        public int diagnosticAiMoves;
        public int diagnosticRootRevision;
        public string failure = "";
        public bool viewpointConversionPassed;
        public float blackRootWhiteMinusBlackScore;
        public float whiteRootWhiteMinusBlackScore;
        public float blackRootWhiteOwnership;
        public float whiteRootWhiteOwnership;

        public int[] actualVisits = new int[CASE_COUNT];
        public int[] selectedMoves = new int[CASE_COUNT];
        public int[] rootOutputRevisions = new int[CASE_COUNT];
        public int[] rootOutputSearchTokens = new int[CASE_COUNT];
        public int[] readerStages = new int[CASE_COUNT];
        public int[] ownershipReadbacks = new int[CASE_COUNT];
        public int[] scenarioMoveCounts = new int[CASE_COUNT];

        public float[] rootPolicySpatial = new float[CASE_COUNT * POLICY_COUNT];
        public float[] rootPolicyPass = new float[CASE_COUNT];
        public float[] rootValue = new float[CASE_COUNT * 3];
        public float[] rootScore = new float[CASE_COUNT * 4];
        public float[] rootOwnership = new float[CASE_COUNT * POLICY_COUNT];
        public float[] rootInputSpatial = new float[CASE_COUNT * GoFeatureEncoder.SPATIAL_COUNT];
        public float[] rootInputGlobal = new float[CASE_COUNT * GoFeatureEncoder.GLOBAL_CHANNELS];

        public int[] scenario0Moves = { 60, 300 };
        public int[] scenario1Moves = { 1, 0, 19 };
        public int[] scenario2Moves = { 180, 181, 161, 199, 179, 201, 160, 220 };
        public int[] scenario3Moves = { 60, 300, 40, 320, 61, 299, 59, 301, 62, 298, 58 };
        public int[] scenario4Moves = { 316, 273 };
        public int[] scenario5Moves = { 151, 150, -1 };
        public int[] scenario6Moves = { 141, 313, 186, 10 };
        public int[] scenario7Moves = { 205, 109, 263, 342, 260 };
        public int firstScenario;
        public int scenarioCount = CASE_COUNT;

        private bool runningScenario;
        private bool encodingRoot;
        private bool rootCaptured;
        private int baselineMoveCount;
        private int baselineAiMoves;

        public void RunNeuralConsistencyProbe()
        {
            probeFinished = false;
            probePassed = false;
            scenarioIndex = firstScenario;
            PublishDiagnostics();
            failure = "";
            blackRootWhiteMinusBlackScore=controller==null?0f:
                controller.ConvertSideToMoveScoreToWhiteMinusBlack(3.5f,GoGame.BLACK);
            whiteRootWhiteMinusBlackScore=controller==null?0f:
                controller.ConvertSideToMoveScoreToWhiteMinusBlack(3.5f,GoGame.WHITE);
            blackRootWhiteOwnership=controller==null?0f:
                controller.ConvertSideToMoveOwnershipLogitToWhitePositive(0.75f,GoGame.BLACK);
            whiteRootWhiteOwnership=controller==null?0f:
                controller.ConvertSideToMoveOwnershipLogitToWhitePositive(0.75f,GoGame.WHITE);
            viewpointConversionPassed=blackRootWhiteMinusBlackScore==-3.5f&&
                whiteRootWhiteMinusBlackScore==3.5f&&
                blackRootWhiteOwnership<0f&&whiteRootWhiteOwnership>0f&&
                -blackRootWhiteOwnership==whiteRootWhiteOwnership;
            for (int i = 0; i < CASE_COUNT; i++)
            {
                actualVisits[i] = 0;
                selectedMoves[i] = GoGame.NONE;
                rootOutputRevisions[i] = 0;
                rootOutputSearchTokens[i] = 0;
                readerStages[i] = 0;
                ownershipReadbacks[i] = 0;
                scenarioMoveCounts[i] = 0;
            }
            for (int i = 0; i < rootPolicySpatial.Length; i++)
            {
                rootPolicySpatial[i] = 0f;
                rootOwnership[i] = 0f;
            }
            for (int i = 0; i < rootValue.Length; i++) rootValue[i] = 0f;
            for (int i = 0; i < rootScore.Length; i++) rootScore[i] = 0f;
            for (int i = 0; i < rootPolicyPass.Length; i++) rootPolicyPass[i] = 0f;

            if (game == null || controller == null || encoder == null || blackDifficulty == null ||
                whiteDifficulty == null)
            {
                Finish("neural output export probe references are incomplete");
                return;
            }
            if(!viewpointConversionPassed)
            {
                Finish("fixed-colour score/ownership viewpoint conversion failed");
                return;
            }
            if (firstScenario < 0 || scenarioCount < 1 ||
                firstScenario + scenarioCount > CASE_COUNT)
            {
                Finish("neural output export scenario range is invalid");
                return;
            }

            controller.autoStart = false;
            game.SetMatchMode(GoAiController.MODE_AIVAI);
            // Keep the default world profile at Beginner/8, while this
            // numerical corpus deliberately uses the Advanced parameter base
            // with a fixed 32-visit Custom override.
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
            PublishDiagnostics();
            if (probeFinished || !runningScenario) return;
            if (game == null || controller == null || encoder == null)
            {
                Finish("neural output export probe lost a runtime reference");
                return;
            }
            if (encodingRoot)
            {
                int encodeState = encoder.StepEncode(2);
                if (encodeState == GoFeatureEncoder.ENCODE_RUNNING)
                    return;
                if (encodeState != GoFeatureEncoder.ENCODE_COMPLETE)
                {
                    Finish("root feature encoding failed for scenario=" + scenarioIndex +
                        ": " + encoder.lastError);
                    return;
                }
                int inputOffset = scenarioIndex * GoFeatureEncoder.SPATIAL_COUNT;
                for (int i = 0; i < GoFeatureEncoder.SPATIAL_COUNT; i++)
                    rootInputSpatial[inputOffset + i] = encoder.spatialOutput[i];
                int globalOffset = scenarioIndex * GoFeatureEncoder.GLOBAL_CHANNELS;
                for (int i = 0; i < GoFeatureEncoder.GLOBAL_CHANNELS; i++)
                    rootInputGlobal[globalOffset + i] = encoder.globalOutput[i];
                encodingRoot = false;
                controller.autoStart = true;
                return;
            }
            if (controller.controllerState == GoAiController.STATE_ERROR)
            {
                Finish("AI controller error: " + controller.lastError);
                return;
            }

            // GoGame invalidates the controller's local root snapshot as soon
            // as an authoritative AI move is committed. Capture the complete
            // first root readback before that commit, then wait for the exact
            // search budget and move transition below.
            if (!rootCaptured && controller.lastRootOutputRevision > 0 &&
                controller.lastRootOutputSearchToken > 0 &&
                controller.lastRootReadbackStages == 1 &&
                controller.lastRootOwnershipReadbacks >= 1)
            {
                CaptureRootOutput();
                rootCaptured = true;
            }
            if (game.moveCount <= baselineMoveCount ||
                controller.aiMoves <= baselineAiMoves) return;

            actualVisits[scenarioIndex] = controller.search == null ? 0 :
                controller.search.visitsCompleted;
            selectedMoves[scenarioIndex] = game.lastMove;
            scenarioMoveCounts[scenarioIndex] = game.moveCount;

            runningScenario = false;
            controller.autoStart = false;
            if (!rootCaptured || actualVisits[scenarioIndex] < DIAGNOSTIC_VISITS ||
                rootOutputRevisions[scenarioIndex] <= 0 ||
                rootOutputSearchTokens[scenarioIndex] <= 0 ||
                readerStages[scenarioIndex] != 1 ||
                ownershipReadbacks[scenarioIndex] < 1)
            {
                Finish("incomplete root output scenario=" + scenarioIndex +
                    " visits=" + actualVisits[scenarioIndex] +
                    " revision=" + rootOutputRevisions[scenarioIndex] +
                    " token=" + rootOutputSearchTokens[scenarioIndex] +
                    " stages=" + readerStages[scenarioIndex] +
                    " ownership=" + ownershipReadbacks[scenarioIndex]);
                return;
            }
            if (scenarioIndex >= firstScenario + scenarioCount - 1)
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
            if (probeFinished) return;
            controller.InvalidateSearch();
            controller.autoStart = false;
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            if (!PlayScenarioMoves(scenarioIndex)) return;
            baselineMoveCount = game.moveCount;
            baselineAiMoves = controller.aiMoves;
            if (!encoder.BeginEncodeGame(game))
            {
                Finish("root feature encoding could not start for scenario=" +
                    scenarioIndex + ": " + encoder.lastError);
                return;
            }
            rootCaptured = false;
            encodingRoot = true;
            runningScenario = true;
        }

        private void CaptureRootOutput()
        {
            int offset = scenarioIndex * POLICY_COUNT;
            for (int i = 0; i < POLICY_COUNT; i++)
            {
                rootPolicySpatial[offset + i] = controller.lastRootPolicySpatial[i];
                rootOwnership[offset + i] = controller.lastRootOwnership[i];
            }
            rootPolicyPass[scenarioIndex] = controller.lastRootPolicyPass;
            for (int i = 0; i < 3; i++)
                rootValue[scenarioIndex * 3 + i] = controller.lastRootValue[i];
            for (int i = 0; i < 4; i++)
                rootScore[scenarioIndex * 4 + i] = controller.lastRootScore[i];
            rootOutputRevisions[scenarioIndex] = controller.lastRootOutputRevision;
            rootOutputSearchTokens[scenarioIndex] = controller.lastRootOutputSearchToken;
            readerStages[scenarioIndex] = controller.lastRootReadbackStages;
            ownershipReadbacks[scenarioIndex] = controller.lastRootOwnershipReadbacks;
        }

        private bool PlayScenarioMoves(int index)
        {
            int[] moves = index == 0 ? scenario0Moves :
                index == 1 ? scenario1Moves :
                index == 2 ? scenario2Moves :
                index == 3 ? scenario3Moves :
                index == 4 ? scenario4Moves :
                index == 5 ? scenario5Moves :
                index == 6 ? scenario6Moves : scenario7Moves;
            if (moves == null || moves.Length < 1)
            {
                Finish("scenario has no fixed moves: " + index);
                return false;
            }
            for (int i = 0; i < moves.Length; i++)
            {
                bool accepted;
                if (moves[i] == GoGame.PASS)
                {
                    int beforeMoveCount = game.moveCount;
                    game.PassFromAI();
                    accepted = game.moveCount == beforeMoveCount + 1;
                }
                else
                {
                    accepted = game.TryPlayFromAI(moves[i]);
                }
                if (!accepted)
                {
                    Finish("fixed sequence illegal in neural output export scenario=" + index +
                        " index=" + i + " location=" + moves[i]);
                    return false;
                }
            }
            return true;
        }

        private void Finish(string message)
        {
            PublishDiagnostics();
            failure = message;
            runningScenario = false;
            encodingRoot = false;
            probeFinished = true;
        }

        private void PublishDiagnostics()
        {
            diagnosticRunningScenario = runningScenario;
            diagnosticEncodingRoot = encodingRoot;
            diagnosticEncodeState = encoder == null ? 0 : encoder.encodeState;
            diagnosticEncodeStage = encoder == null ? 0 : encoder.encodeStage;
            diagnosticControllerState = controller == null ? 0 : controller.controllerState;
            diagnosticSearchPhase = controller == null || controller.search == null ? 0 :
                controller.search.phase;
            diagnosticGameMoveCount = game == null ? 0 : game.moveCount;
            diagnosticAiMoves = controller == null ? 0 : controller.aiMoves;
            diagnosticRootRevision = controller == null ? 0 : controller.lastRootOutputRevision;
        }
    }
}
