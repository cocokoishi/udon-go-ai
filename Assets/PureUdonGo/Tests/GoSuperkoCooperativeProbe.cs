using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// Serialized-Udon regression for the neural-leaf superko preparation
    /// stage. It builds a long legal history, creates an independent GoGame
    /// reference mask cooperatively, waits for one complete GPU neural leaf,
    /// then invalidates the following mask while it is in progress.
    /// </summary>
    public sealed class GoSuperkoCooperativeProbe : UdonSharpBehaviour
    {
        private const int PHASE_IDLE=0;
        private const int PHASE_BUILD_HISTORY=1;
        private const int PHASE_REFERENCE_MASK=2;
        private const int PHASE_WAIT_FIRST_MASK=3;
        private const int PHASE_WAIT_ROOT_OUTPUT=4;
        private const int PHASE_WAIT_SECOND_MASK=5;
        private const int PHASE_VERIFY_CANCEL=6;

        public GoGame game;
        public GoAiController controller;
        public int[] moves=
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
        public int phase;
        public int historyMoveIndex;
        public int referenceCursor;
        public bool[] referenceMask=new bool[GoGame.AREA];
        public int firstMaskPoints;
        public int firstMaskFrames;
        public float firstMaskMilliseconds;
        public int observedMaxPointsOneFrame;
        public float observedMaxFrameMilliseconds;
        public int maskMismatches;
        public int rootStateCopiedInts;
        public int rootHistoryCopiedEntries;
        public int simulationUndoOperations;
        public int historyIndexEntries;
        public int historyIndexMismatches;
        public int historyBucketEntries;
        public int historyEntriesExamined;
        public int completedNeuralLeafStages;
        public int cancelledAtPoint;
        public int pointsAtCancellation;
        public int pointsAfterCancellation;
        public int cancellationFramesObserved;
        public int revisionBeforeCancellation;
        public int revisionAfterCancellation;
        public int searchTokenBeforeCancellation;
        public int cancelledProbeCount;
        public string failure="";

        private int cancelledMaskSignature;
        private int[] expectedHistoryStoneCounts=new int[GoGame.AREA+1];

        public void RunSuperkoCooperativeProbe()
        {
            probeFinished=false;probePassed=false;failure="";
            phase=PHASE_IDLE;historyMoveIndex=0;referenceCursor=0;
            firstMaskPoints=0;firstMaskFrames=0;firstMaskMilliseconds=0f;
            observedMaxPointsOneFrame=0;observedMaxFrameMilliseconds=0f;
            maskMismatches=0;completedNeuralLeafStages=0;cancelledAtPoint=0;
            rootStateCopiedInts=0;rootHistoryCopiedEntries=0;simulationUndoOperations=0;
            historyIndexEntries=0;historyIndexMismatches=0;
            historyBucketEntries=0;historyEntriesExamined=0;
            pointsAtCancellation=0;pointsAfterCancellation=0;
            cancellationFramesObserved=0;revisionBeforeCancellation=0;
            revisionAfterCancellation=0;searchTokenBeforeCancellation=0;
            cancelledProbeCount=0;cancelledMaskSignature=0;
            for(int i=0;i<GoGame.AREA;i++)referenceMask[i]=false;
            if(game==null||controller==null||controller.search==null||
                controller.pendingSuperkoMask==null||
                controller.pendingSuperkoMask.Length<GoGame.AREA||
                moves==null||moves.Length!=160)
            {
                Finish("superko cooperative probe references are incomplete");
                return;
            }
            controller.autoStart=false;
            controller.InvalidateSearch();
            game.SetMatchMode(GoAiController.MODE_AIVAI);
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            phase=PHASE_BUILD_HISTORY;
        }

        public void Update()
        {
            if(probeFinished||phase==PHASE_IDLE)return;
            if(phase==PHASE_BUILD_HISTORY)
            {
                if(historyMoveIndex>=moves.Length)
                {
                    if(game.positionHistoryCount<161)
                    {
                        Finish("long history did not retain every position");
                        return;
                    }
                    phase=PHASE_REFERENCE_MASK;
                    return;
                }
                int move=moves[historyMoveIndex];
                int before=game.moveCount;
                bool accepted;
                if(move==GoGame.PASS)
                {
                    game.PassFromAI();
                    accepted=game.moveCount==before+1;
                }
                else accepted=game.TryPlayFromAI(move);
                if(!accepted)
                {
                    Finish("long-history move rejected index="+historyMoveIndex+
                        " location="+move);
                    return;
                }
                historyMoveIndex++;
                return;
            }
            if(phase==PHASE_REFERENCE_MASK)
            {
                // Keep the test oracle itself bounded; its purpose is to
                // compare the production simulation mask, not add a test-only
                // 361-point frame spike.
                int end=referenceCursor+16;
                if(end>GoGame.AREA)end=GoGame.AREA;
                while(referenceCursor<end)
                {
                    referenceMask[referenceCursor]=game.IsSuperkoBanned(referenceCursor);
                    referenceCursor++;
                }
                if(referenceCursor<GoGame.AREA)return;
                controller.autoStart=true;
                phase=PHASE_WAIT_FIRST_MASK;
                return;
            }
            if(phase==PHASE_WAIT_FIRST_MASK)
            {
                if(controller.completedSuperkoProbeMasks<1)return;
                firstMaskPoints=controller.superkoProbePoints;
                firstMaskFrames=controller.superkoProbeFrames;
                firstMaskMilliseconds=controller.superkoProbeMilliseconds;
                observedMaxPointsOneFrame=controller.maxSuperkoPointsOneFrame;
                observedMaxFrameMilliseconds=controller.maxSuperkoFrameMilliseconds;
                for(int i=0;i<GoGame.AREA;i++)
                    if(referenceMask[i]!=controller.pendingSuperkoMask[i])maskMismatches++;
                if(firstMaskPoints!=GoGame.AREA||firstMaskFrames<2||
                    observedMaxPointsOneFrame>=GoGame.AREA||maskMismatches!=0)
                {
                    Finish("first mask was not cooperative/equivalent points="+
                        firstMaskPoints+" frames="+firstMaskFrames+" max="+
                        observedMaxPointsOneFrame+" mismatches="+maskMismatches);
                    return;
                }
                phase=PHASE_WAIT_ROOT_OUTPUT;
                return;
            }
            if(phase==PHASE_WAIT_ROOT_OUTPUT)
            {
                if(controller.lastRootOutputRevision<=0||
                    controller.lastRootOutputSearchToken<=0||
                    controller.lastRootReadbackStages!=1)return;
                completedNeuralLeafStages=controller.lastRootReadbackStages;
                rootStateCopiedInts=controller.search.rootStateCopiedInts;
                rootHistoryCopiedEntries=controller.search.rootHistoryCopiedEntries;
                simulationUndoOperations=controller.search.simulationUndoOperations;
                historyIndexEntries=controller.search.simulationState==null?0:
                    controller.search.simulationState.positionHistoryCount;
                historyIndexMismatches=VerifyHistoryIndex(controller.search.simulationState);
                historyEntriesExamined=controller.search.simulationState==null?0:
                    controller.search.simulationState.historyEntriesExamined;
                if(rootHistoryCopiedEntries!=161||rootStateCopiedInts>=12000||
                    historyIndexMismatches!=0||historyBucketEntries!=historyIndexEntries)
                {
                    Finish("root history copy was not bounded entries="+
                        rootHistoryCopiedEntries+" ints="+rootStateCopiedInts+
                        " historyIndexMismatches="+historyIndexMismatches);
                    return;
                }
                phase=PHASE_WAIT_SECOND_MASK;
                return;
            }
            if(phase==PHASE_WAIT_SECOND_MASK)
            {
                if(!controller.superkoProbeActive||
                    controller.superkoProbePoints<=GoGame.AREA||
                    controller.currentSuperkoProbeIndex<=0||
                    controller.currentSuperkoProbeIndex>=GoGame.AREA)return;
                controller.autoStart=false;
                cancelledAtPoint=controller.currentSuperkoProbeIndex;
                pointsAtCancellation=controller.superkoProbePoints;
                revisionBeforeCancellation=game.revision;
                searchTokenBeforeCancellation=controller.search.searchToken;
                game.RequestForceReset();
                revisionAfterCancellation=game.revision;
                pointsAfterCancellation=controller.superkoProbePoints;
                cancelledProbeCount=controller.cancelledSuperkoProbes;
                cancelledMaskSignature=MaskSignature(controller.pendingSuperkoMask);
                phase=PHASE_VERIFY_CANCEL;
                return;
            }
            if(phase==PHASE_VERIFY_CANCEL)
            {
                cancellationFramesObserved++;
                pointsAfterCancellation=controller.superkoProbePoints;
                if(cancellationFramesObserved<4)return;
                bool stopped=pointsAfterCancellation==pointsAtCancellation&&
                    !controller.superkoProbeActive&&
                    controller.currentSuperkoProbeIndex==0&&
                    MaskSignature(controller.pendingSuperkoMask)==cancelledMaskSignature;
                bool invalidated=revisionAfterCancellation>revisionBeforeCancellation&&
                    controller.search.phase==GoMctsSearch.PHASE_CANCELLED&&
                    cancelledProbeCount>0&&
                    controller.lastCancelledSuperkoProbeIndex==cancelledAtPoint;
                if(!stopped||!invalidated)
                {
                    Finish("stale superko preparation continued stopped="+stopped+
                        " invalidated="+invalidated+" points="+pointsAtCancellation+
                        "->"+pointsAfterCancellation+" cancelledAt="+cancelledAtPoint+
                        " revision="+revisionBeforeCancellation+"->"+
                        revisionAfterCancellation);
                    return;
                }
                probePassed=true;probeFinished=true;phase=PHASE_IDLE;
            }
        }

        private int MaskSignature(bool[] mask)
        {
            if(mask==null)return -1;
            int signature=17;
            for(int i=0;i<GoGame.AREA;i++)
                signature=signature*31+(mask[i]?i+1:0);
            return signature;
        }

        private int VerifyHistoryIndex(GoSearchState state)
        {
            if(state==null||!state.historyStoneCountFrequencyValid||
                state.historyStoneCount==null||state.historyStoneCountFrequency==null)
                return 1;
            for(int i=0;i<=GoGame.AREA;i++)expectedHistoryStoneCounts[i]=0;
            int count=state.positionHistoryCount;
            if(count<0||count>GoGame.MAX_POSITION_HISTORY)return 1;
            for(int i=0;i<count;i++)
            {
                int stones=state.historyStoneCount[i];
                if(stones<0||stones>GoGame.AREA)return 1;
                expectedHistoryStoneCounts[stones]++;
            }
            int mismatches=0;
            for(int i=0;i<=GoGame.AREA;i++)
                if(expectedHistoryStoneCounts[i]!=state.historyStoneCountFrequency[i])mismatches++;
            historyBucketEntries=0;
            if(state.historyBucketHead==null||state.historyBucketNext==null)return mismatches+1;
            for(int stones=0;stones<=GoGame.AREA;stones++)
            {
                int head=state.historyBucketHead[stones];int guard=0;
                while(head!=0&&guard<=count)
                {
                    int index=head-1;
                    if(index<0||index>=count||state.historyStoneCount[index]!=stones)
                    {mismatches++;break;}
                    historyBucketEntries++;guard++;head=state.historyBucketNext[index];
                }
                if(guard>count)mismatches++;
            }
            return mismatches;
        }

        private void Finish(string message)
        {
            controller.autoStart=false;
            failure=message;
            phase=PHASE_IDLE;
            probeFinished=true;
        }
    }
}
