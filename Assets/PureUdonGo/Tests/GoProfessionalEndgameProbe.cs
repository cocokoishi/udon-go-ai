using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Runs a fixed set of late-game positions extracted from attributed
    /// professional SGF records.  The editor verifier injects the flattened
    /// move logs from professional-endgames.json before compiling this real
    /// Udon program. The focused parity commands run exactly 8 and 48 visits,
    /// matching the public product calibration anchors. No oracle or fake
    /// result is produced in Udon.
    /// </summary>
    public sealed class GoProfessionalEndgameProbe : UdonSharpBehaviour
    {
        public const int POSITION_COUNT = 32;
        public const int BUDGET_COUNT = 2;
        public const int BENCHMARK_BEGINNER_VISITS = 8;
        public const int BENCHMARK_ADVANCED_VISITS = 48;
        public const int SAMPLE_COUNT = POSITION_COUNT * BUDGET_COUNT;
        public const int ROOT_CANDIDATE_COUNT = 10;
        public const int DECISION_TRACE_CAPACITY = 64;
        public const int RAW_ROOT_TIE_CAPACITY = GoMctsSearch.POLICY_SIZE;

        private const int PHASE_IDLE = 0;
        private const int PHASE_PREPARE = 1;
        private const int PHASE_BUILD = 2;
        private const int PHASE_WAIT_SEARCH = 3;

        public GoGame game;
        public GoAiController controller;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiSettings aiSettings;

        // The editor verifier injects these from the checked-in manifest.
        public string[] positionIds = new string[POSITION_COUNT];
        public int[] positionMoveOffsets = new int[POSITION_COUNT];
        public int[] positionMoveCounts = new int[POSITION_COUNT];
        public int[] flattenedMoves = new int[0];
        public int positionStart;
        public int positionLimit = POSITION_COUNT;
        public int budgetStart;
        public int budgetLimit = BUDGET_COUNT;
        public bool captureReferenceTrace;
        public int referenceTraceCapacity = 8;
        public int searchSemanticsMode;

        public int[] targetVisits =
        {
            BENCHMARK_BEGINNER_VISITS,
            BENCHMARK_ADVANCED_VISITS
        };

        public bool probeFinished;
        public bool probePassed;
        public bool runningSample;
        public int positionIndex;
        public int budgetIndex;
        public int sampleIndex;
        public int phase;
        public int moveCursor;
        public int baselineMoveCount;
        public int baselineAiMoves;
        public float sampleStartedAt;
        public int sampleStartedFrame;
        // Live watchdog snapshot. These are updated only while the real
        // controller is searching, so a hard timeout can distinguish a slow
        // but progressing position from a liveness stall without changing the
        // search path or allocating per-frame diagnostics.
        public int activeSearchPhase;
        public int activeSearchVisits;
        public int activeSearchTargetVisits;
        public int activeSearchTreeNodes;
        public int activeSearchTreeEdges;
        public int activeSearchStepCalls;
        public int activeLegalMoveChecks;
        public int activeSimulationPlayCalls;
        public int activeHistoryEntriesExamined;
        public int activeHistoryQueryCount;
        public int activeHistoryBucketHitCount;
        public int activeHistoryBucketMissCount;
        public int activeHistoryFullScanCount;
        public int activeHistoryMaxEntriesExamined;
        public int activeLadderSearchTransitions;
        public int activeLadderNodes;
        public int activeGameMoveCount;
        public int activeGameRevision;
        public int activeControllerAiMoves;
        public int activeBaselineMoveCount;
        public int activeBaselineAiMoves;
        public int activeControllerState;
        public bool activeControllerAutoStart;
        public float activeSearchElapsedSeconds;
        public string failure = "";

        public int[] selectedMoves = new int[SAMPLE_COUNT];
        // The product-selected move is temperature sampled. Keep it separate
        // from the deterministic root ranking used by search-fidelity checks.
        public int[] sampledSelectedMoves = new int[SAMPLE_COUNT];
        public int[] deterministicRootMoves = new int[SAMPLE_COUNT];
        public int[] deterministicRootVisits = new int[SAMPLE_COUNT];
        public int[] policyMoves = new int[SAMPLE_COUNT];
        public int[] actualVisits = new int[SAMPLE_COUNT];
        public int[] targetSearchVisits = new int[SAMPLE_COUNT];
        public int[] moveCounts = new int[SAMPLE_COUNT];
        public int[] terminalStates = new int[SAMPLE_COUNT];
        public bool[] aiResigned = new bool[SAMPLE_COUNT];
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
        public int[] rootCandidateMoves =
            new int[SAMPLE_COUNT * ROOT_CANDIDATE_COUNT];
        public int[] rootCandidateVisits =
            new int[SAMPLE_COUNT * ROOT_CANDIDATE_COUNT];
        public int[] rawRootVisitArgmaxVisits = new int[SAMPLE_COUNT];
        public int[] rawRootVisitArgmaxTieCounts = new int[SAMPLE_COUNT];
        public int[] rawRootVisitArgmaxMoves = new int[
            SAMPLE_COUNT * RAW_ROOT_TIE_CAPACITY];

        // Completed-sample stage counters used by the long-history profiler.
        // They are observations copied from GoAiController after a genuine
        // search completes; none of them participates in move selection.
        public int[] searchFrames = new int[SAMPLE_COUNT];
        public int[] searchStepCalls = new int[SAMPLE_COUNT];
        public int[] legalMoveChecks = new int[SAMPLE_COUNT];
        public int[] simulationPlayCalls = new int[SAMPLE_COUNT];
        public int[] historyEntriesExamined = new int[SAMPLE_COUNT];
        public int[] historyQueryCount = new int[SAMPLE_COUNT];
        public int[] historyBucketHitCount = new int[SAMPLE_COUNT];
        public int[] historyBucketMissCount = new int[SAMPLE_COUNT];
        public int[] historyFullScanCount = new int[SAMPLE_COUNT];
        public int[] historyMaxEntriesExamined = new int[SAMPLE_COUNT];
        public int[] ladderStepCalls = new int[SAMPLE_COUNT];
        public int[] ladderSearchTransitions = new int[SAMPLE_COUNT];
        public int[] ladderNodes = new int[SAMPLE_COUNT];
        public int[] gpuPasses = new int[SAMPLE_COUNT];
        public int[] gpuGraphFrames = new int[SAMPLE_COUNT];
        public int[] readbackRequests = new int[SAMPLE_COUNT];
        public float[] searchMilliseconds = new float[SAMPLE_COUNT];
        public float[] featureMilliseconds = new float[SAMPLE_COUNT];
        public float[] ladderMilliseconds = new float[SAMPLE_COUNT];
        public float[] scoreUtilityMilliseconds = new float[SAMPLE_COUNT];
        public float[] gpuEvaluationMilliseconds = new float[SAMPLE_COUNT];
        public float[] gpuDispatchMilliseconds = new float[SAMPLE_COUNT];
        public float[] readbackMilliseconds = new float[SAMPLE_COUNT];

        // Root-selection trace copied out before the controller starts the
        // next sample. This is diagnostic-only and bounded to the 8/48 visit
        // audit; it never participates in product search decisions.
        // Allocated only for an explicit root-trace run. Keeping these null
        // for ordinary probes avoids adding megabytes to the normal ClientSim
        // fixture and keeps the diagnostic path out of production memory.
        public int[] referenceDecisionCounts;
        public int[] referenceDecisionSimulationIndex;
        public int[] referenceDecisionParentVisits;
        public int[] referenceDecisionSelectedMove;
        public int[] referenceDecisionSelectedEdgeVisits;
        public float[] referenceDecisionPolicyMassVisited;
        public float[] referenceDecisionTotalChildWeight;
        public float[] referenceDecisionFpu;
        public float[] referenceDecisionExploration;
        public float[] referenceDecisionBestScore;
        public float[] referenceDecisionSecondBestScore;
        public float[] referenceDecisionSelectedPrior;
        public float[] referenceDecisionSelectedQ;
        public float[] referenceDecisionSelectedU;
        public float[] referenceDecisionSelectedScore;
        public int[] referenceDecisionCandidateMoves;
        public int[] referenceDecisionCandidateVisits;
        public float[] referenceDecisionCandidatePrior;
        public float[] referenceDecisionCandidateQ;
        public float[] referenceDecisionCandidateU;
        public float[] referenceDecisionCandidateScore;

        private bool rootObservationCaptured;

        private void EnsureDecisionTraceStorage()
        {
            if (!captureReferenceTrace || referenceDecisionCounts != null)
                return;
            int sampleCapacity = SAMPLE_COUNT;
            int decisionCapacity = sampleCapacity * DECISION_TRACE_CAPACITY;
            int candidateCapacity = decisionCapacity *
                GoMctsSearch.REFERENCE_DECISION_CANDIDATE_COUNT;
            referenceDecisionCounts = new int[sampleCapacity];
            referenceDecisionSimulationIndex = new int[decisionCapacity];
            referenceDecisionParentVisits = new int[decisionCapacity];
            referenceDecisionSelectedMove = new int[decisionCapacity];
            referenceDecisionSelectedEdgeVisits = new int[decisionCapacity];
            referenceDecisionPolicyMassVisited = new float[decisionCapacity];
            referenceDecisionTotalChildWeight = new float[decisionCapacity];
            referenceDecisionFpu = new float[decisionCapacity];
            referenceDecisionExploration = new float[decisionCapacity];
            referenceDecisionBestScore = new float[decisionCapacity];
            referenceDecisionSecondBestScore = new float[decisionCapacity];
            referenceDecisionSelectedPrior = new float[decisionCapacity];
            referenceDecisionSelectedQ = new float[decisionCapacity];
            referenceDecisionSelectedU = new float[decisionCapacity];
            referenceDecisionSelectedScore = new float[decisionCapacity];
            referenceDecisionCandidateMoves = new int[candidateCapacity];
            referenceDecisionCandidateVisits = new int[candidateCapacity];
            referenceDecisionCandidatePrior = new float[candidateCapacity];
            referenceDecisionCandidateQ = new float[candidateCapacity];
            referenceDecisionCandidateU = new float[candidateCapacity];
            referenceDecisionCandidateScore = new float[candidateCapacity];
            for (int i = 0; i < decisionCapacity; i++)
            {
                referenceDecisionSimulationIndex[i] = -1;
                referenceDecisionSelectedMove[i] = GoGame.NONE;
            }
            for (int i = 0; i < candidateCapacity; i++)
                referenceDecisionCandidateMoves[i] = GoGame.NONE;
        }

        public void RunProfessionalEndgameProbe()
        {
            probeFinished = false;
            probePassed = false;
            runningSample = false;
            positionIndex = 0;
            budgetIndex = 0;
            sampleIndex = 0;
            phase = PHASE_PREPARE;
            moveCursor = 0;
            rootObservationCaptured = false;
            EnsureDecisionTraceStorage();
            failure = "";
            positionStart = Mathf.Clamp(positionStart, 0, POSITION_COUNT - 1);
            positionLimit = Mathf.Clamp(positionLimit, positionStart + 1, POSITION_COUNT);
            budgetStart = Mathf.Clamp(budgetStart, 0, BUDGET_COUNT - 1);
            budgetLimit = Mathf.Clamp(budgetLimit, budgetStart + 1, BUDGET_COUNT);
            // A focused 8/48 run only owns the requested sample rectangle.  The
            // backing arrays are intentionally full-corpus sized for schema
            // compatibility, but older generated probe assets may contain a
            // shorter optional reference-decision array.  Reset only the
            // active range so a partial run cannot index past that serialized
            // diagnostic buffer (and never touches an unrequested budget
            // samples).
            int activeSampleCount = (positionLimit - positionStart) *
                (budgetLimit - budgetStart);
            positionIndex = positionStart;
            budgetIndex = budgetStart;
            activeSearchPhase = PHASE_IDLE;
            activeSearchVisits = 0;
            activeSearchTargetVisits = 0;
            activeSearchTreeNodes = 0;
            activeSearchTreeEdges = 0;
            activeSearchStepCalls = 0;
            activeLegalMoveChecks = 0;
            activeSimulationPlayCalls = 0;
            activeHistoryEntriesExamined = 0;
            activeHistoryQueryCount = 0;
            activeHistoryBucketHitCount = 0;
            activeHistoryBucketMissCount = 0;
            activeHistoryFullScanCount = 0;
            activeHistoryMaxEntriesExamined = 0;
            activeLadderSearchTransitions = 0;
            activeLadderNodes = 0;
            activeGameMoveCount = 0;
            activeGameRevision = 0;
            activeControllerAiMoves = 0;
            activeBaselineMoveCount = 0;
            activeBaselineAiMoves = 0;
            activeControllerState = GoAiController.STATE_IDLE;
            activeControllerAutoStart = false;
            activeSearchElapsedSeconds = 0f;
            if (positionIds == null || positionIds.Length != POSITION_COUNT ||
                positionMoveOffsets == null || positionMoveOffsets.Length != POSITION_COUNT ||
                positionMoveCounts == null || positionMoveCounts.Length != POSITION_COUNT ||
                flattenedMoves == null || targetVisits == null ||
                targetVisits.Length != BUDGET_COUNT)
            {
                Finish("professional endgame corpus arrays are incomplete");
                return;
            }
            if (game == null || controller == null || blackDifficulty == null ||
                whiteDifficulty == null)
            {
                Finish("professional endgame runtime references are incomplete");
                return;
            }
            for (int i = 0; i < POSITION_COUNT; i++)
            {
                if (positionMoveOffsets[i] < 0 || positionMoveCounts[i] < 1 ||
                    positionMoveOffsets[i] + positionMoveCounts[i] > flattenedMoves.Length)
                {
                    Finish("invalid move range for corpus position " + i);
                    return;
                }
            }
            for (int i = 0; i < activeSampleCount; i++)
            {
                selectedMoves[i] = GoGame.NONE;
                sampledSelectedMoves[i] = GoGame.NONE;
                deterministicRootMoves[i] = GoGame.NONE;
                deterministicRootVisits[i] = 0;
                policyMoves[i] = GoGame.NONE;
                actualVisits[i] = 0;
                targetSearchVisits[i] = 0;
                moveCounts[i] = 0;
                terminalStates[i] = GoGame.STATE_PLAYING;
                aiResigned[i] = false;
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
                searchFrames[i] = 0;
                searchStepCalls[i] = 0;
                legalMoveChecks[i] = 0;
                simulationPlayCalls[i] = 0;
                historyEntriesExamined[i] = 0;
                historyQueryCount[i] = 0;
                historyBucketHitCount[i] = 0;
                historyBucketMissCount[i] = 0;
                historyFullScanCount[i] = 0;
                historyMaxEntriesExamined[i] = 0;
                ladderStepCalls[i] = 0;
                ladderSearchTransitions[i] = 0;
                ladderNodes[i] = 0;
                gpuPasses[i] = 0;
                gpuGraphFrames[i] = 0;
                readbackRequests[i] = 0;
                searchMilliseconds[i] = 0f;
                featureMilliseconds[i] = 0f;
                ladderMilliseconds[i] = 0f;
                scoreUtilityMilliseconds[i] = 0f;
                gpuEvaluationMilliseconds[i] = 0f;
                gpuDispatchMilliseconds[i] = 0f;
                readbackMilliseconds[i] = 0f;
                rawRootVisitArgmaxVisits[i] = 0;
                rawRootVisitArgmaxTieCounts[i] = 0;
                for (int tie = 0; tie < RAW_ROOT_TIE_CAPACITY; tie++)
                    rawRootVisitArgmaxMoves[i * RAW_ROOT_TIE_CAPACITY + tie] = GoGame.NONE;
                if (referenceDecisionCounts != null &&
                    i < referenceDecisionCounts.Length)
                    referenceDecisionCounts[i] = 0;
                for (int candidate = 0; candidate < ROOT_CANDIDATE_COUNT; candidate++)
                {
                    rootCandidateMoves[i * ROOT_CANDIDATE_COUNT + candidate] =
                        GoGame.NONE;
                    rootCandidateVisits[i * ROOT_CANDIDATE_COUNT + candidate] = 0;
                }
            }
            controller.autoStart = false;
            if(controller.search!=null)
                controller.search.ConfigureSearchSemantics(searchSemanticsMode);
            if(captureReferenceTrace&&controller.search!=null)
                controller.search.ConfigureReferenceTrace(referenceTraceCapacity);
            controller.InvalidateSearch();
        }

        public void Update()
        {
            if (probeFinished || phase == PHASE_IDLE)
                return;
            if (game == null || controller == null)
            {
                Finish("professional endgame runtime reference was lost");
                return;
            }
            if (controller.controllerState == GoAiController.STATE_ERROR)
            {
                Finish("AI controller error: " + controller.lastError);
                return;
            }
            if (phase == PHASE_PREPARE)
            {
                controller.autoStart = false;
                controller.InvalidateSearch();
                game.positionalSuperko = true;
                game.multiStoneSuicideLegal = true;
                game.areaScoring = true;
                game.SetMatchMode(GoAiController.MODE_AIVAI);
                int visits = targetVisits[budgetIndex];
                int preset = budgetIndex == 0 ? GoDifficultyProfile.BEGINNER :
                    budgetIndex == 1 ? GoDifficultyProfile.ADVANCED :
                    GoDifficultyProfile.MASTER;
                if (aiSettings != null)
                {
                    if (!aiSettings.ApplyPackedVisits(preset, visits, visits))
                    {
                        Finish("settings Apply denied for position=" + positionIndex +
                            " budget=" + visits);
                        return;
                    }
                }
                else
                {
                    blackDifficulty.ApplyResolvedLocal(preset, visits, 0, true);
                    whiteDifficulty.ApplyResolvedLocal(preset, visits, 0, true);
                }
                game.RequestForceReset();
                game.SetBlackStarts();
                game.RequestStartMatch();
                moveCursor = 0;
                phase = PHASE_BUILD;
                return;
            }
            if (phase == PHASE_BUILD)
            {
                int offset = positionMoveOffsets[positionIndex];
                int count = positionMoveCounts[positionIndex];
                if (moveCursor < count)
                {
                    int chunk = Mathf.Min(8, count - moveCursor);
                    if (!game.ReplayMovesForVerifier(flattenedMoves,
                        offset + moveCursor, chunk))
                    {
                        Finish("source replay rejected position=" + positionIds[positionIndex] +
                            " ply=" + moveCursor);
                        return;
                    }
                    moveCursor += chunk;
                    if (moveCursor < count)
                        return;
                    baselineMoveCount = game.moveCount;
                    baselineAiMoves = controller.aiMoves;
                    sampleStartedAt = Time.realtimeSinceStartup;
                    sampleStartedFrame = Time.frameCount;
                    activeSearchPhase = PHASE_IDLE;
                    activeSearchVisits = 0;
                    activeSearchTargetVisits = targetVisits[budgetIndex];
                    activeSearchTreeNodes = 0;
                    activeSearchTreeEdges = 0;
                    activeSearchStepCalls = 0;
                    activeLegalMoveChecks = 0;
                    activeSimulationPlayCalls = 0;
                    activeHistoryEntriesExamined = 0;
                    activeHistoryQueryCount = 0;
                    activeHistoryBucketHitCount = 0;
                    activeHistoryBucketMissCount = 0;
                    activeHistoryFullScanCount = 0;
                    activeHistoryMaxEntriesExamined = 0;
                    activeLadderSearchTransitions = 0;
                    activeLadderNodes = 0;
                    activeGameMoveCount = game.moveCount;
                    activeGameRevision = game.revision;
                    activeControllerAiMoves = controller.aiMoves;
                    activeBaselineMoveCount = baselineMoveCount;
                    activeBaselineAiMoves = baselineAiMoves;
                    activeControllerState = controller.controllerState;
                    activeControllerAutoStart = controller.autoStart;
                    activeSearchElapsedSeconds = 0f;
                    startFrames[sampleIndex] = sampleStartedFrame;
                    rootObservationCaptured = false;
                    controller.autoStart = true;
                    runningSample = true;
                    phase = PHASE_WAIT_SEARCH;
                    return;
                }
                moveCursor++;
                return;
            }
            // CommitSearchMove clears the controller's local root identity as
            // soon as the authoritative move is accepted. Capture that
            // evidence while the real readback result is still current, then
            // use the captured values after the move commit below.
            if (!rootObservationCaptured &&
                controller.lastRootOutputRevision > 0 &&
                controller.lastRootOutputSearchToken > 0 &&
                controller.lastRootReadbackStages >= 1 &&
                controller.lastRootOwnershipReadbacks > 0)
            {
                rootOutputRevisions[sampleIndex] = controller.lastRootOutputRevision;
                rootOutputSearchTokens[sampleIndex] = controller.lastRootOutputSearchToken;
                readerStages[sampleIndex] = controller.lastRootReadbackStages;
                ownershipReadbacks[sampleIndex] = controller.lastRootOwnershipReadbacks;
                rootObservationCaptured = true;
            }
            bool aiActionCompleted = controller.aiMoves > baselineAiMoves &&
                (game.moveCount > baselineMoveCount ||
                 game.gameState != GoGame.STATE_PLAYING);
            if (!runningSample || !aiActionCompleted)
            {
                CaptureActiveProgress();
                return;
            }

            int index = sampleIndex;
            GoMctsSearch search = controller.search;
            GoGpuNeuralOutputReader reader = controller.reader;
            // A configured resign is a valid terminal AI action and does not
            // append a Go move. Preserve the searched best move for the
            // consistency artifact while marking the terminal outcome.
            int observedMove = game.lastMove;
            bool resigned = game.gameState == GoGame.STATE_RESIGNED;
            if (resigned && search != null)
                observedMove = search.bestMove;
            selectedMoves[index] = observedMove;
            sampledSelectedMoves[index] = observedMove;
            moveCounts[index] = game.moveCount;
            terminalStates[index] = game.gameState;
            aiResigned[index] = resigned;
            if (search != null)
            {
                deterministicRootMoves[index] = search.GetDeterministicRootMove();
                deterministicRootVisits[index] = search.GetDeterministicRootVisits();
                actualVisits[index] = search.visitsCompleted;
                targetSearchVisits[index] = search.targetVisits;
                treeNodes[index] = search.treeNodeCount;
                treeEdges[index] = search.treeEdgeCount;
                selectedRootVisits[index] = search.GetRootVisitsForMove(observedMove);
                policyMoves[index] = search.GetRootPriorMove();
                rootMeanValues[index] = search.GetRootMeanValue();
                searchFrames[index] = controller.lastSearchFrames;
                searchStepCalls[index] = controller.lastSearchStepCalls;
                legalMoveChecks[index] = controller.lastLegalMoveChecks;
                simulationPlayCalls[index] = controller.lastSimulationPlayCalls;
                historyEntriesExamined[index] = controller.lastHistoryEntriesExamined;
                historyQueryCount[index] = controller.lastHistoryQueryCount;
                historyBucketHitCount[index] = controller.lastHistoryBucketHitCount;
                historyBucketMissCount[index] = controller.lastHistoryBucketMissCount;
                historyFullScanCount[index] = controller.lastHistoryFullScanCount;
                historyMaxEntriesExamined[index] = controller.lastHistoryMaxEntriesExamined;
                ladderStepCalls[index] = controller.lastLadderStepCalls;
                ladderSearchTransitions[index] = controller.lastLadderSearchTransitions;
                ladderNodes[index] = controller.lastLadderNodes;
                gpuPasses[index] = controller.lastGpuPasses;
                gpuGraphFrames[index] = controller.lastGpuGraphFrames;
                readbackRequests[index] = controller.lastReadbackRequests;
                searchMilliseconds[index] = controller.lastSearchMilliseconds;
                featureMilliseconds[index] = controller.lastFeatureMilliseconds;
                ladderMilliseconds[index] = controller.lastLadderMilliseconds;
                scoreUtilityMilliseconds[index] = controller.lastScoreUtilityMilliseconds;
                gpuEvaluationMilliseconds[index] = controller.lastGpuEvaluationMilliseconds;
                gpuDispatchMilliseconds[index] = controller.lastGpuDispatchMilliseconds;
                readbackMilliseconds[index] = controller.lastReadbackMilliseconds;
                for (int candidate = 0; candidate < ROOT_CANDIDATE_COUNT; candidate++)
                {
                    int candidateMove = search.GetRootCandidateMove(candidate);
                    rootCandidateMoves[index * ROOT_CANDIDATE_COUNT + candidate] =
                        candidateMove;
                    rootCandidateVisits[index * ROOT_CANDIDATE_COUNT + candidate] =
                        candidateMove == GoGame.NONE ? 0 :
                        search.GetRootCandidateVisits(candidate);
                }
                rawRootVisitArgmaxVisits[index] = search.GetRootVisitArgmaxVisits();
                int rawTieCount = Mathf.Min(search.GetRootVisitArgmaxTieCount(),
                    RAW_ROOT_TIE_CAPACITY);
                rawRootVisitArgmaxTieCounts[index] = rawTieCount;
                for (int tie = 0; tie < rawTieCount; tie++)
                    rawRootVisitArgmaxMoves[index * RAW_ROOT_TIE_CAPACITY + tie] =
                        search.GetRootVisitArgmaxMove(tie);
                CaptureReferenceDecisions(index, search);
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
            elapsedSeconds[index] = Time.realtimeSinceStartup - sampleStartedAt;

            int expected = targetVisits[budgetIndex];
            if (actualVisits[index] != expected || targetSearchVisits[index] != expected ||
                treeNodes[index] < expected || selectedRootVisits[index] <= 0 ||
                readerStages[index] < 1 || ownershipReadbacks[index] <= 0 ||
                rootOutputRevisions[index] <= 0 || rootOutputSearchTokens[index] <= 0)
            {
                Finish("incomplete endgame sample=" + index +
                    " id=" + positionIds[positionIndex] +
                    " target=" + expected + " visits=" + actualVisits[index] +
                    " searchTarget=" + targetSearchVisits[index] +
                    " nodes=" + treeNodes[index] +
                    " selectedRootVisits=" + selectedRootVisits[index] +
                    " stages=" + readerStages[index] +
                    " ownership=" + ownershipReadbacks[index]);
                return;
            }

            runningSample = false;
            controller.autoStart = false;
            if (positionIndex < positionLimit - 1)
            {
                positionIndex++;
            }
            else if (budgetIndex < budgetLimit - 1)
            {
                positionIndex = positionStart;
                budgetIndex++;
            }
            else
            {
                probePassed = true;
                probeFinished = true;
                phase = PHASE_IDLE;
                return;
            }
            sampleIndex++;
            phase = PHASE_PREPARE;
        }

        private void Finish(string message)
        {
            controller.autoStart = false;
            failure = message;
            probePassed = false;
            probeFinished = true;
            runningSample = false;
            phase = PHASE_IDLE;
        }

        private void CaptureActiveProgress()
        {
            if (!runningSample || controller == null || controller.search == null)
                return;
            GoMctsSearch search = controller.search;
            activeSearchPhase = search.phase;
            activeSearchVisits = search.visitsCompleted;
            activeSearchTargetVisits = search.targetVisits;
            activeSearchTreeNodes = search.treeNodeCount;
            activeSearchTreeEdges = search.treeEdgeCount;
            activeSearchStepCalls = controller.lastSearchStepCalls;
            activeLegalMoveChecks = controller.lastLegalMoveChecks;
            activeSimulationPlayCalls = controller.lastSimulationPlayCalls;
            activeHistoryEntriesExamined = controller.lastHistoryEntriesExamined;
            activeHistoryQueryCount = controller.lastHistoryQueryCount;
            activeHistoryBucketHitCount = controller.lastHistoryBucketHitCount;
            activeHistoryBucketMissCount = controller.lastHistoryBucketMissCount;
            activeHistoryFullScanCount = controller.lastHistoryFullScanCount;
            activeHistoryMaxEntriesExamined = controller.lastHistoryMaxEntriesExamined;
            activeLadderSearchTransitions = controller.lastLadderSearchTransitions;
            activeLadderNodes = controller.lastLadderNodes;
            activeGameMoveCount = game.moveCount;
            activeGameRevision = game.revision;
            activeControllerAiMoves = controller.aiMoves;
            activeBaselineMoveCount = baselineMoveCount;
            activeBaselineAiMoves = baselineAiMoves;
            activeControllerState = controller.controllerState;
            activeControllerAutoStart = controller.autoStart;
            activeSearchElapsedSeconds =
                Time.realtimeSinceStartup - sampleStartedAt;
        }

        private void CaptureReferenceDecisions(int index, GoMctsSearch search)
        {
            if (search == null || !search.referenceTraceEnabled ||
                search.referenceDecisionSimulationIndex == null ||
                search.referenceDecisionCandidateMoves == null ||
                referenceDecisionCounts == null || index < 0 ||
                index >= referenceDecisionCounts.Length)
                return;
            int count = Mathf.Min(search.referenceDecisionCount,
                DECISION_TRACE_CAPACITY);
            referenceDecisionCounts[index] = count;
            int sourceCandidates = GoMctsSearch.REFERENCE_DECISION_CANDIDATE_COUNT;
            int sampleBase = index * DECISION_TRACE_CAPACITY;
            int sourceCount = search.referenceDecisionSimulationIndex.Length;
            for (int i = 0; i < count && i < sourceCount; i++)
            {
                int dst = sampleBase + i;
                referenceDecisionSimulationIndex[dst] =
                    search.referenceDecisionSimulationIndex[i];
                referenceDecisionParentVisits[dst] =
                    search.referenceDecisionParentVisits[i];
                referenceDecisionSelectedMove[dst] =
                    search.referenceDecisionSelectedMove[i];
                referenceDecisionSelectedEdgeVisits[dst] =
                    search.referenceDecisionSelectedEdgeVisits[i];
                referenceDecisionPolicyMassVisited[dst] =
                    search.referenceDecisionPolicyMassVisited[i];
                referenceDecisionTotalChildWeight[dst] =
                    search.referenceDecisionTotalChildWeight[i];
                referenceDecisionFpu[dst] = search.referenceDecisionFpu[i];
                referenceDecisionExploration[dst] =
                    search.referenceDecisionExploration[i];
                referenceDecisionBestScore[dst] =
                    search.referenceDecisionBestScore[i];
                referenceDecisionSecondBestScore[dst] =
                    search.referenceDecisionSecondBestScore[i];
                referenceDecisionSelectedPrior[dst] =
                    search.referenceDecisionSelectedPrior[i];
                referenceDecisionSelectedQ[dst] =
                    search.referenceDecisionSelectedQ[i];
                referenceDecisionSelectedU[dst] =
                    search.referenceDecisionSelectedU[i];
                referenceDecisionSelectedScore[dst] =
                    search.referenceDecisionSelectedScore[i];
                int sourceBase = i * sourceCandidates;
                int candidateBase = (sampleBase + i) * sourceCandidates;
                for (int candidate = 0; candidate < sourceCandidates; candidate++)
                {
                    referenceDecisionCandidateMoves[candidateBase + candidate] =
                        search.referenceDecisionCandidateMoves[sourceBase + candidate];
                    referenceDecisionCandidateVisits[candidateBase + candidate] =
                        search.referenceDecisionCandidateVisits[sourceBase + candidate];
                    referenceDecisionCandidatePrior[candidateBase + candidate] =
                        search.referenceDecisionCandidatePrior[sourceBase + candidate];
                    referenceDecisionCandidateQ[candidateBase + candidate] =
                        search.referenceDecisionCandidateQ[sourceBase + candidate];
                    referenceDecisionCandidateU[candidateBase + candidate] =
                        search.referenceDecisionCandidateU[sourceBase + candidate];
                    referenceDecisionCandidateScore[candidateBase + candidate] =
                        search.referenceDecisionCandidateScore[sourceBase + candidate];
                }
            }
        }
    }
}
