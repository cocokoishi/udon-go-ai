using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only public-ladder corpus producer. Each sample is a real
    /// frame-scheduled Udon GPU/async-readback/PUCT turn over one fixed Go
    /// position. The editor verifier exports observations; an external tool
    /// compares them with the same-model KataGo oracle.
    /// </summary>
    public sealed class GoSearchCalibrationProbe : UdonSharpBehaviour
    {
        public const int SCENARIO_COUNT = 4;
        public const int PROFILE_COUNT = 4;
        public const int SAMPLE_COUNT = SCENARIO_COUNT * PROFILE_COUNT;

        public GoGame game;
        public GoAiController controller;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiSettings aiSettings;

        public bool probeFinished;
        public bool probePassed;
        public bool runningSample;
        public int scenarioIndex;
        public int profileIndex;
        public int sampleIndex;
        public int scenarioLimit = SCENARIO_COUNT;
        public int profileStart;
        public int profileLimit = PROFILE_COUNT;
        public int currentSearchVisits;
        public int currentSearchPhase;
        public string failure = "";

        public int[] targetVisits =
        {
            GoDifficultyProfile.BEGINNER_VISITS,
            GoDifficultyProfile.ADVANCED_VISITS,
            GoDifficultyProfile.MASTER_VISITS,
            GoDifficultyProfile.ULTRAHARD_VISITS
        };
        public int[] selectedMoves = new int[SAMPLE_COUNT];
        public int[] policyMoves = new int[SAMPLE_COUNT];
        public int[] actualVisits = new int[SAMPLE_COUNT];
        public int[] targetSearchVisits = new int[SAMPLE_COUNT];
        public int[] moveCounts = new int[SAMPLE_COUNT];
        public int[] treeNodes = new int[SAMPLE_COUNT];
        public int[] treeEdges = new int[SAMPLE_COUNT];
        public int[] selectedRootVisits = new int[SAMPLE_COUNT];
        public int[] readerStages = new int[SAMPLE_COUNT];
        public int[] ownershipReadbacks = new int[SAMPLE_COUNT];
        public int[] rootOutputRevisions = new int[SAMPLE_COUNT];
        public int[] rootOutputSearchTokens = new int[SAMPLE_COUNT];
        public int[] startFrames = new int[SAMPLE_COUNT];
        public int[] endFrames = new int[SAMPLE_COUNT];
        public float[] elapsedSeconds = new float[SAMPLE_COUNT];
        public float[] rootMeanValues = new float[SAMPLE_COUNT];

        public int[] scenario0Moves = { 60, 300 };
        public int[] scenario1Moves = { 1, 0, 19 };
        public int[] scenario2Moves = { 180, 181, 161, 199, 179, 201, 160, 220 };
        public int[] scenario3Moves = { 60, 300, 40, 320, 61, 299, 59, 301, 62, 298, 58 };

        private int baselineMoveCount;
        private int baselineAiMoves;
        private float sampleStartSeconds;
        private int sampleStartFrame;
        private bool rootObservationCaptured;

        public void RunSearchCalibrationProbe()
        {
            probeFinished = false;
            probePassed = false;
            runningSample = false;
            scenarioIndex = 0;
            profileStart = Mathf.Clamp(profileStart, 0, PROFILE_COUNT - 1);
            profileIndex = profileStart;
            sampleIndex = 0;
            if (scenarioLimit < 1 || scenarioLimit > SCENARIO_COUNT)
                scenarioLimit = SCENARIO_COUNT;
            if (profileLimit <= profileStart || profileLimit > PROFILE_COUNT)
                profileLimit = PROFILE_COUNT;
            currentSearchVisits = 0;
            currentSearchPhase = GoMctsSearch.PHASE_IDLE;
            rootObservationCaptured = false;
            failure = "";
            for (int i = 0; i < SAMPLE_COUNT; i++)
            {
                selectedMoves[i] = GoGame.NONE;
                policyMoves[i] = GoGame.NONE;
                actualVisits[i] = 0;
                targetSearchVisits[i] = 0;
                moveCounts[i] = 0;
                treeNodes[i] = 0;
                treeEdges[i] = 0;
                selectedRootVisits[i] = 0;
                readerStages[i] = 0;
                ownershipReadbacks[i] = 0;
                rootOutputRevisions[i] = 0;
                rootOutputSearchTokens[i] = 0;
                startFrames[i] = 0;
                endFrames[i] = 0;
                elapsedSeconds[i] = 0f;
                rootMeanValues[i] = 0f;
            }
            if (game == null || controller == null || blackDifficulty == null ||
                whiteDifficulty == null || targetVisits.Length != PROFILE_COUNT)
            {
                Finish("search calibration probe references or profiles are incomplete");
                return;
            }
            controller.autoStart = false;
            game.SetMatchMode(GoAiController.MODE_AIVAI);
            BeginSample();
        }

        public void Update()
        {
            if (probeFinished || !runningSample)
                return;
            if (game == null || controller == null)
            {
                Finish("search calibration probe lost a runtime reference");
                return;
            }
            if (controller.controllerState == GoAiController.STATE_ERROR)
            {
                Finish("AI controller error: " + controller.lastError);
                return;
            }
            if (controller.search != null)
            {
                currentSearchVisits = controller.search.visitsCompleted;
                currentSearchPhase = controller.search.phase;
            }
            // CommitSearchMove invalidates the controller's local root
            // snapshot immediately after the authoritative move. Capture the
            // root identity and readback evidence while it is still valid so
            // a successful search is not reported as incomplete afterward.
            if (!rootObservationCaptured &&
                controller.lastRootOutputRevision > 0 &&
                controller.lastRootOutputSearchToken > 0 &&
                controller.lastRootReadbackStages == 1 &&
                controller.lastRootOwnershipReadbacks > 0)
            {
                int rootIndex = sampleIndex;
                rootOutputRevisions[rootIndex] = controller.lastRootOutputRevision;
                rootOutputSearchTokens[rootIndex] = controller.lastRootOutputSearchToken;
                readerStages[rootIndex] = controller.lastRootReadbackStages;
                ownershipReadbacks[rootIndex] = controller.lastRootOwnershipReadbacks;
                if (controller.search != null)
                    rootMeanValues[rootIndex] = controller.search.GetRootMeanValue();
                rootObservationCaptured = true;
            }
            if (game.moveCount <= baselineMoveCount ||
                controller.aiMoves <= baselineAiMoves)
                return;

            int index = sampleIndex;
            GoMctsSearch search = controller.search;
            GoGpuNeuralOutputReader reader = controller.reader;
            selectedMoves[index] = game.lastMove;
            moveCounts[index] = game.moveCount;
            if (search != null)
            {
                actualVisits[index] = search.visitsCompleted;
                targetSearchVisits[index] = search.targetVisits;
                treeNodes[index] = search.treeNodeCount;
                treeEdges[index] = search.treeEdgeCount;
                selectedRootVisits[index] = search.GetRootVisitsForMove(game.lastMove);
                policyMoves[index] = search.GetRootPriorMove();
                rootMeanValues[index] = search.GetRootMeanValue();
            }
            if (reader != null && !rootObservationCaptured)
            {
                readerStages[index] = reader.completedStages;
                ownershipReadbacks[index] = reader.ownershipReadbackCount;
            }
            if (!rootObservationCaptured)
            {
                rootOutputRevisions[index] = controller.lastRootOutputRevision;
                rootOutputSearchTokens[index] = controller.lastRootOutputSearchToken;
            }
            endFrames[index] = Time.frameCount;
            elapsedSeconds[index] = Time.realtimeSinceStartup - sampleStartSeconds;

            int expectedVisits = targetVisits[profileIndex];
            if (actualVisits[index] != expectedVisits ||
                targetSearchVisits[index] != expectedVisits ||
                treeNodes[index] < expectedVisits ||
                selectedRootVisits[index] <= 0 ||
                readerStages[index] != 1 ||
                ownershipReadbacks[index] <= 0 ||
                rootOutputRevisions[index] <= 0 ||
                rootOutputSearchTokens[index] <= 0)
            {
                Finish("incomplete public ladder sample=" + index +
                    " target=" + expectedVisits +
                    " visits=" + actualVisits[index] +
                    " searchTarget=" + targetSearchVisits[index] +
                    " nodes=" + treeNodes[index] +
                    " selectedRootVisits=" + selectedRootVisits[index] +
                    " stages=" + readerStages[index] +
                    " ownership=" + ownershipReadbacks[index]);
                return;
            }

            runningSample = false;
            controller.autoStart = false;
            if (profileIndex < profileLimit - 1)
            {
                profileIndex++;
            }
            else if (scenarioIndex < scenarioLimit - 1)
            {
                scenarioIndex++;
                profileIndex = profileStart;
            }
            else
            {
                probePassed = true;
                probeFinished = true;
                return;
            }
            sampleIndex++;
            BeginSample();
        }

        private void BeginSample()
        {
            controller.InvalidateSearch();
            controller.autoStart = false;
            ApplyProfile(profileIndex);
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            if (!PlayScenarioMoves(scenarioIndex))
                return;
            baselineMoveCount = game.moveCount;
            baselineAiMoves = controller.aiMoves;
            sampleStartSeconds = Time.realtimeSinceStartup;
            sampleStartFrame = Time.frameCount;
            startFrames[sampleIndex] = sampleStartFrame;
            rootObservationCaptured = false;
            runningSample = true;
            controller.autoStart = true;
        }

        private void ApplyProfile(int index)
        {
            if(aiSettings!=null)
            {
                int visits=targetVisits[Mathf.Clamp(index,0,PROFILE_COUNT-1)];
                aiSettings.ApplyPackedVisits(index,-visits,-visits);
            }
            else
            {
                blackDifficulty.ApplyResolvedLocal(index,targetVisits[index],0,false);
                whiteDifficulty.ApplyResolvedLocal(index,targetVisits[index],0,false);
            }
        }

        private bool PlayScenarioMoves(int index)
        {
            int[] moves = index == 0 ? scenario0Moves :
                index == 1 ? scenario1Moves :
                index == 2 ? scenario2Moves : scenario3Moves;
            if (moves == null || moves.Length < 1)
            {
                Finish("search calibration scenario is empty: " + index);
                return false;
            }
            for (int i = 0; i < moves.Length; i++)
            {
                if (!game.TryPlayFromAI(moves[i]))
                {
                    Finish("fixed search calibration move rejected scenario=" + index +
                        " index=" + i + " location=" + moves[i]);
                    return false;
                }
            }
            return true;
        }

        private void Finish(string message)
        {
            runningSample = false;
            failure = message;
            probeFinished = true;
        }
    }
}
