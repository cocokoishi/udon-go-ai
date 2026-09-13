using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Fixed A-H single-ClientSim performance corpus.  It reuses the move
    /// sequences already covered by the search-calibration, feature and
    /// long-history probes and runs each fixture at fixed diagnostic budgets
    /// of exactly 8 and 48 visits, matching the public Beginner/Master
    /// calibration anchors rather than an obsolete diagnostic budget.
    /// The probe records stage telemetry; it never changes the requested
    /// budgets or skips a feature/rule stage to improve timing.
    /// </summary>
    public sealed class GoPerformanceCorpusProbe : UdonSharpBehaviour
    {
        public const int BEGINNER_TEST_VISITS=8;
        public const int ADVANCED_TEST_VISITS=48;
        public const int FIXTURE_COUNT=8;
        public const int BUDGET_COUNT=2;
        public const int SAMPLE_COUNT=FIXTURE_COUNT*BUDGET_COUNT;
        private const int PREPARE=1,BUILD=2,WAIT_SEARCH=3;

        public GoGame game;
        public GoAiController controller;
        public GoAiSettings aiSettings;
        public GoBoardPool boardPool;
        public GoFeatureEncoder encoder;

        public int[] targetVisits={BEGINNER_TEST_VISITS, ADVANCED_TEST_VISITS};
        public int fixtureIndex;
        public int budgetIndex;
        public int sampleIndex;
        public int phase;
        public int moveCursor;
        public int baselineMoveCount;
        public int baselineAiMoves;
        public float sampleStartedAt;
        public int sampleStartedFrame;
        public bool probeFinished;
        public bool probePassed;
        public string failure="";

        public int[] actualVisits=new int[SAMPLE_COUNT];
        public int[] searchFrames=new int[SAMPLE_COUNT];
        public int[] gpuPasses=new int[SAMPLE_COUNT];
        public int[] graphFrames=new int[SAMPLE_COUNT];
        public int[] readbackRequests=new int[SAMPLE_COUNT];
        public int[] historyEntriesExamined=new int[SAMPLE_COUNT];
        public int[] featureSteps=new int[SAMPLE_COUNT];
        public int[] ladderStepCalls=new int[SAMPLE_COUNT];
        public int[] ladderTransitions=new int[SAMPLE_COUNT];
        public int[] ladderNodes=new int[SAMPLE_COUNT];
        public int[] mctsSelectionCalls=new int[SAMPLE_COUNT];
        public int[] legalMoveChecks=new int[SAMPLE_COUNT];
        public int[] scoreUtilitySamples=new int[SAMPLE_COUNT];
        public int[] rootStateCopiedInts=new int[SAMPLE_COUNT];
        public int[] rootHistoryCopiedEntries=new int[SAMPLE_COUNT];
        public int[] moveMaskPoints=new int[SAMPLE_COUNT];
        public int[] moveMaskFrames=new int[SAMPLE_COUNT];
        public int[] moveMaskHits=new int[SAMPLE_COUNT];
        public int[] moveMaskMisses=new int[SAMPLE_COUNT];
        public int[] simulationUndoOperations=new int[SAMPLE_COUNT];
        // Relative completion frames for the first four visits. These are
        // deliberately separate from total searchFrames so a cold-start cliff
        // cannot disappear inside an average.
        public int[] firstVisitCompletionFrames=new int[SAMPLE_COUNT*4];
        public int[] sameFramePhaseTransitions=new int[SAMPLE_COUNT];
        public int[] avoidableYields=new int[SAMPLE_COUNT];
        public int[] deadlineYields=new int[SAMPLE_COUNT];
        public int[] asyncReadbackWaitFrames=new int[SAMPLE_COUNT];
        public int[] telemetryCommitsDuringSearch=new int[SAMPLE_COUNT];
        public int[] telemetrySerializationsDuringSearch=new int[SAMPLE_COUNT];
        public int[] moveMaskSuppressedCriticalFrames=new int[SAMPLE_COUNT];
        public float[] grantedCpuBudgetMilliseconds=new float[SAMPLE_COUNT];
        public float[] usedCpuBudgetMilliseconds=new float[SAMPLE_COUNT];
        public float[] maxCpuBudgetOvershootMilliseconds=new float[SAMPLE_COUNT];
        public float[] gpuSubmitLastFrameMilliseconds=new float[SAMPLE_COUNT];
        public float[] wallMilliseconds=new float[SAMPLE_COUNT];
        public float[] millisecondsPerVisit=new float[SAMPLE_COUNT];
        public float[] superkoMilliseconds=new float[SAMPLE_COUNT];
        public float[] featureMilliseconds=new float[SAMPLE_COUNT];
        public float[] mctsMilliseconds=new float[SAMPLE_COUNT];
        public float[] mctsRootCopyMilliseconds=new float[SAMPLE_COUNT];
        public float[] mctsExpansionMilliseconds=new float[SAMPLE_COUNT];
        public float[] mctsBackupMilliseconds=new float[SAMPLE_COUNT];
        public float[] scoreUtilityMilliseconds=new float[SAMPLE_COUNT];
        public float[] mctsSelectionMilliseconds=new float[SAMPLE_COUNT];
        public float[] ladderMilliseconds=new float[SAMPLE_COUNT];
        public float[] controllerMaxTickMilliseconds=new float[SAMPLE_COUNT];
        public float[] moveMaskMaxFrameMilliseconds=new float[SAMPLE_COUNT];
        public float[] featureSuperkoMilliseconds=new float[SAMPLE_COUNT];
        public float[] featureClearMilliseconds=new float[SAMPLE_COUNT];
        public float[] featureBaseMilliseconds=new float[SAMPLE_COUNT];
        public float[] featureAreaMilliseconds=new float[SAMPLE_COUNT];
        public float[] inputPackingMilliseconds=new float[SAMPLE_COUNT];
        public float[] spatialApplyMilliseconds=new float[SAMPLE_COUNT];
        public float[] globalApplyMilliseconds=new float[SAMPLE_COUNT];
        public float[] inputBlitMilliseconds=new float[SAMPLE_COUNT];
        public float[] gpuSubmitMilliseconds=new float[SAMPLE_COUNT];
        public float[] gpuSmoothedSubmitMilliseconds=new float[SAMPLE_COUNT];
        public float[] gpuMaxSubmitMilliseconds=new float[SAMPLE_COUNT];
        public float[] readbackMilliseconds=new float[SAMPLE_COUNT];
        public float[] uiRefreshMilliseconds=new float[SAMPLE_COUNT];
        public float[] frameP50Milliseconds=new float[SAMPLE_COUNT];
        public float[] frameP95Milliseconds=new float[SAMPLE_COUNT];
        public float[] frameP99Milliseconds=new float[SAMPLE_COUNT];
        public float[] frameMaxMilliseconds=new float[SAMPLE_COUNT];
        public const int FRAME_SAMPLE_CAPACITY=2048;
        // Raw samples are copied in linear time. Percentile sorting belongs in
        // the Editor verifier so the Udon probe cannot create an unmeasured
        // O(n^2) spike at the end of every corpus sample.
        public int[] frameSampleCounts=new int[SAMPLE_COUNT];
        public float[] frameSamples=new float[SAMPLE_COUNT*FRAME_SAMPLE_CAPACITY];
        public int[] fixtureSequenceHashes=new int[FIXTURE_COUNT];

        // Existing deterministic fixture sources.
        public int[] fixtureAEmpty={};
        public int[] fixtureBOpening={60,300};
        public int[] fixtureCTactical={180,181,161,199,179,201,160,220};
        public int[] fixtureFSuperko={60,300,72,288,40,320,54,59,61,299,91,269,43,317,111,235,148,119,354,7,79,173,181,197,223,105,90,208,175,2,115,65,304,164,36,254,152,53,129,338,117,76,312,201,167,240,191,337,135,67,-1,74,262,276,114,315,17,44,37,270,333,16,274,62,3,318,243,204};
        public int[] fixtureGLadder={60,300,72,288,40,320,54,59,61,299,91,269,43,317,111,235,63,297,129,231};
        // D/E/H are frozen legal move logs. They were generated once from the
        // long-history seeds and then checked against GoGame before being
        // committed here. Runtime measurement never searches for a different
        // legal move, so before/after samples are byte-for-byte comparable.
        public int[] fixtureDDense={
            69,93,238,20,217,60,337,268,259,237,64,77,340,94,11,263,135,5,48,272,
            160,299,298,26,121,327,252,41,44,24,318,348,221,148,293,129,165,223,78,57,
            38,218,97,51,274,170,303,100,311,102,62,149,329,275,246,207,47,178,119,215,
            133,128,326,224,156,188,53,335,290,279,109,243,355,73,295,229,211,172,113,351,
            32,316,123,346,310,74,124,126,99,264,66,95,58,334,206,281,98,136,306,322,161,
            132,202,91,103,68,167,292,222,354,31,25,72,213,122,111,107,92,176,16,239,106,
            273,347,344,171,253,83};
        public int[] fixtureELong={
            281,148,337,90,301,227,170,114,173,250,306,343,316,16,290,201,142,131,299,95,
            164,358,32,324,175,330,155,130,289,38,318,58,171,1,309,243,93,228,259,323,184,
            50,63,121,8,233,267,13,92,162,195,359,278,61,194,336,55,280,344,60,161,137,41,
            197,265,321,109,248,56,80,29,19,116,36,360,98,126,101,349,223,9,348,37,204,158,
            317,345,96,220,34,6,297,257,192,273,7,35,295,151,333,347,132,352,64,255,272,105,
            89,87,188,165,216,354,279,266,229,76,251,77,141,47,199,53,20,231,215,135,186,275,
            332,196,312,159,304,84,254,134,99,320,286,147,110,22,334,86,133,4,353,311,181,156,
            78,287,33,237,189,94,207,283,325,205,326,62,302,314,153,179,14,75,21,191,122,183,
            356,328,341,219,308,224,27,157,69,236,187,269,247,107,83,209,313,108,152,249,351,
            54,100,270,42,238,103,168,15,339,274,28,352,357,178,252,138,245,45,3,307,106,120,
            218,88,212,115,230,2,163,260,128,293,24,284,65,129,271,25,146,296,167,240,39,160,
            169,46,256,350,176,143,91,145,172,66,327,217,68,211,85,52,154,232};
        public int[] fixtureHEndgame={
            329,360,226,296,9,45,162,136,158,14,263,90,
            187,64,95,8,160,154,20,192,73,180,11,184,
            258,207,202,293,119,236,182,232,141,304,153,156,
            116,65,283,200,208,142,174,299,354,59,171,43,
            28,336,70,191,32,144,150,165,302,41,328,159,
            230,109,139,93,52,118,63,146,249,131,112,74,
            325,56,231,351,107,327,301,280,15,220,330,88,
            106,339,239,163,78,30,61,115,161,148,135,240,
            338,246,343,291,133,126,76,190,1,196,49,255,
            26,22,189,155,103,212,46,261,111,85,272,253,
            337,355,257,104,0,166,21,178,81,177,245,347,
            36,314,270,242,206,123,198,130,267,121,129,122,
            134,114,254,72,211,322,294,308,38,170,60,313,
            260,252,310,358,323,292,167,113,67,278,53,227,
            221,289,51,37,58,87,210,75,286,324,305,268,
            279,132,172,335,128,39,315,42,91,271,80,35,
            24,350,68,217,33,86,277,307,138,105,99,275,
            359,40,110,273,218,201,251,312,125,344,62,250,
            332,117,124,100,213,306,284,168,269,183,353,235,
            309,84,102,151,97,34,282,108,47,31,234,254,
            13,244,266,349,3,44,285,311,346,205,69,288,
            5,295,199,274,194,272,12,4,173,222,215,204,
            71,209,233,54,303,316,193,223,89,326,356,298,
            348,79,55,101,238,352,300,334,57,224,82,179,
            25,195,23,276,181,243,96,248,186,214,287,228,
            219,225,247,340,176,127,232,220,229,149,209,83,
            103,290,203,164,241,27,331,324,48,262,342,145,
            4,357,354,317,192,359,216,143,264,304,157,66,
            18,169,265,297,125,50,16,197,152,115,201,237,
            250,175,140,10,2,7,102,147,159,294,190,114,
            103,319,214,196,347,29,29,6,120,27,197,333,
            248,6,106,343,98,30,180,179,178,323,7,31,
            191,315,17,320,124,256,220,188,177,92,246,72,
            6,107,179,111,110,150,318,325,35,281,125,356,
            338,195,54,321,19,342,337,195,353,259,227,129,
            260,226,263,265,246,303,124,283,189,185,102,301,
            200,208,212,137,175,156,196,155,282,101,318,106,
            264,318,136,94,100,102,284,345,195,112,331,347,
            77,309,302,338,124,189,114,279,265,103,328,332,
            227,206,348,337,346,330,137,347,117,154,187,167,
            310,241,283,341,156,353,8,260,27,245,265,354,
            10,300,154,125,346,50,329,128,282,91,263,31,
            302,30,283,284,14,124,268,50,115,331,34,227,
            50,110,246,264};

        private int fixtureSequenceHash;
        private int sampleMoveMaskSuppressedCriticalBaseline;

        public void RunPerformanceCorpusProbe()
        {
            probeFinished=false;probePassed=false;failure="";
            fixtureIndex=0;budgetIndex=0;sampleIndex=0;phase=PREPARE;moveCursor=0;
            if(game==null||controller==null||encoder==null||
                targetVisits==null||targetVisits.Length!=BUDGET_COUNT||
                fixtureDDense==null||fixtureDDense.Length!=128||
                fixtureELong==null||fixtureELong.Length!=256||
                fixtureHEndgame==null||fixtureHEndgame.Length!=520)
            {Finish("performance corpus references are incomplete");return;}
            if(aiSettings==null)aiSettings=game.aiSettings;
            controller.autoStart=false;controller.InvalidateSearch();
            for(int i=0;i<SAMPLE_COUNT;i++)
            {
                actualVisits[i]=0;searchFrames[i]=0;gpuPasses[i]=0;graphFrames[i]=0;
                readbackRequests[i]=0;historyEntriesExamined[i]=0;featureSteps[i]=0;
                ladderStepCalls[i]=0;ladderTransitions[i]=0;ladderNodes[i]=0;
                mctsSelectionCalls[i]=0;legalMoveChecks[i]=0;scoreUtilitySamples[i]=0;
                rootStateCopiedInts[i]=0;rootHistoryCopiedEntries[i]=0;
                moveMaskPoints[i]=0;moveMaskFrames[i]=0;moveMaskHits[i]=0;moveMaskMisses[i]=0;
                simulationUndoOperations[i]=0;wallMilliseconds[i]=0f;
                millisecondsPerVisit[i]=0f;superkoMilliseconds[i]=0f;featureMilliseconds[i]=0f;
                mctsMilliseconds[i]=0f;mctsRootCopyMilliseconds[i]=0f;
                mctsExpansionMilliseconds[i]=0f;mctsBackupMilliseconds[i]=0f;
                scoreUtilityMilliseconds[i]=0f;inputPackingMilliseconds[i]=0f;
                mctsSelectionMilliseconds[i]=0f;ladderMilliseconds[i]=0f;
                controllerMaxTickMilliseconds[i]=0f;moveMaskMaxFrameMilliseconds[i]=0f;
                featureSuperkoMilliseconds[i]=0f;featureClearMilliseconds[i]=0f;
                featureBaseMilliseconds[i]=0f;featureAreaMilliseconds[i]=0f;
                spatialApplyMilliseconds[i]=0f;globalApplyMilliseconds[i]=0f;
                inputBlitMilliseconds[i]=0f;gpuSubmitMilliseconds[i]=0f;
                gpuSmoothedSubmitMilliseconds[i]=0f;gpuMaxSubmitMilliseconds[i]=0f;
                readbackMilliseconds[i]=0f;
                uiRefreshMilliseconds[i]=0f;frameP50Milliseconds[i]=0f;
                frameP95Milliseconds[i]=0f;frameP99Milliseconds[i]=0f;
                frameMaxMilliseconds[i]=0f;
                sameFramePhaseTransitions[i]=0;avoidableYields[i]=0;
                deadlineYields[i]=0;asyncReadbackWaitFrames[i]=0;
                telemetryCommitsDuringSearch[i]=0;
                telemetrySerializationsDuringSearch[i]=0;
                moveMaskSuppressedCriticalFrames[i]=0;
                grantedCpuBudgetMilliseconds[i]=0f;
                usedCpuBudgetMilliseconds[i]=0f;
                maxCpuBudgetOvershootMilliseconds[i]=0f;
                gpuSubmitLastFrameMilliseconds[i]=0f;
                int visitFrameBase=i*4;
                for(int v=0;v<4;v++)firstVisitCompletionFrames[visitFrameBase+v]=-1;
                frameSampleCounts[i]=0;
            }
        }

        public void Update()
        {
            if(probeFinished||phase==0)return;
            if(controller!=null&&controller.controllerState==GoAiController.STATE_ERROR)
            {Finish("performance corpus search error sample="+sampleIndex+" "+controller.lastError);return;}
            if(phase==PREPARE)
            {
                controller.autoStart=false;controller.InvalidateSearch();
                game.positionalSuperko=true;game.multiStoneSuicideLegal=true;game.areaScoring=true;
                game.SetMatchMode(GoAiController.MODE_AIVAI);
                int visits=targetVisits[budgetIndex];
                if(aiSettings!=null)
                {
                    int publicPreset=budgetIndex==1?
                        GoDifficultyProfile.ADVANCED:GoDifficultyProfile.BEGINNER;
                    if(!aiSettings.ApplyPackedVisits(publicPreset,
                        visits,visits))
                    {Finish("performance corpus settings Apply denied sample="+sampleIndex);return;}
                }
                game.RequestForceReset();game.SetBlackStarts();game.RequestStartMatch();
                moveCursor=0;fixtureSequenceHash=17;phase=BUILD;
                return;
            }
            if(phase==BUILD)
            {
                int[] moves=GetFixtureMoves(fixtureIndex);
                int target=moves.Length;
                if(moveCursor>=target)
                {
                    if(budgetIndex==0)fixtureSequenceHashes[fixtureIndex]=fixtureSequenceHash;
                    else if(fixtureSequenceHashes[fixtureIndex]!=fixtureSequenceHash)
                    {Finish("fixture sequence drifted between product 8 and 48 visits fixture="+fixtureIndex);return;}
                    baselineMoveCount=game.moveCount;baselineAiMoves=controller.aiMoves;
                    game.ResetMoveMaskTelemetry();
                    sampleMoveMaskSuppressedCriticalBaseline=boardPool==null?0:
                        boardPool.moveMaskWarmupSuppressedCriticalFrames;
                    if(boardPool!=null)boardPool.ResetFrameSamples();
                    if(game.ui!=null)game.ui.ResetRefreshTiming();
                    sampleStartedAt=Time.realtimeSinceStartup;sampleStartedFrame=Time.frameCount;
                    controller.autoStart=true;phase=WAIT_SEARCH;return;
                }
                int move=moves[moveCursor++];int before=game.moveCount;bool accepted;
                if(move==GoGame.PASS){game.PassFromAI();accepted=game.moveCount==before+1;}
                else accepted=game.TryPlayFromAI(move);
                if(!accepted){Finish("performance corpus fixture move rejected fixture="+fixtureIndex+" index="+(moveCursor-1));return;}
                RecordFixtureMove(move);
                return;
            }
            if(game.moveCount<=baselineMoveCount||controller.aiMoves<=baselineAiMoves)return;
            int index=sampleIndex;GoMctsSearch search=controller.search;
            actualVisits[index]=controller.lastSearchCompletedVisits;
            searchFrames[index]=controller.lastSearchFrames;
            int visitFrameBase=index*4;
            firstVisitCompletionFrames[visitFrameBase]=controller.visit0CompletionFrame;
            firstVisitCompletionFrames[visitFrameBase+1]=controller.visit1CompletionFrame;
            firstVisitCompletionFrames[visitFrameBase+2]=controller.visit2CompletionFrame;
            firstVisitCompletionFrames[visitFrameBase+3]=controller.visit3CompletionFrame;
            sameFramePhaseTransitions[index]=controller.sameFramePhaseTransitions;
            avoidableYields[index]=controller.avoidableYieldCount;
            deadlineYields[index]=controller.deadlineYieldCount;
            asyncReadbackWaitFrames[index]=controller.asyncReadbackWaitFrames;
            telemetryCommitsDuringSearch[index]=controller.telemetryCommitsDuringSearch;
            telemetrySerializationsDuringSearch[index]=controller.telemetrySerializationsDuringSearch;
            moveMaskSuppressedCriticalFrames[index]=boardPool==null?0:
                Mathf.Max(0,boardPool.moveMaskWarmupSuppressedCriticalFrames-
                    sampleMoveMaskSuppressedCriticalBaseline);
            grantedCpuBudgetMilliseconds[index]=controller.grantedFrameWorkMs;
            usedCpuBudgetMilliseconds[index]=controller.usedFrameWorkMs;
            maxCpuBudgetOvershootMilliseconds[index]=controller.maxFrameWorkOvershootMs;
            gpuPasses[index]=controller.lastGpuPasses;graphFrames[index]=controller.lastGpuGraphFrames;
            readbackRequests[index]=controller.lastReadbackRequests;
            historyEntriesExamined[index]=controller.lastHistoryEntriesExamined;
            featureSteps[index]=controller.lastFeatureSteps;
            ladderStepCalls[index]=controller.lastLadderStepCalls;
            ladderTransitions[index]=controller.lastLadderSearchTransitions;
            ladderNodes[index]=controller.lastLadderNodes;
            mctsSelectionCalls[index]=controller.lastSearchStepCalls;
            legalMoveChecks[index]=controller.lastLegalMoveChecks;
            scoreUtilitySamples[index]=search==null?0:search.scoreUtilitySamples;
            rootStateCopiedInts[index]=search==null?0:search.rootStateCopiedInts;
            rootHistoryCopiedEntries[index]=search==null?0:search.rootHistoryCopiedEntries;
            moveMaskPoints[index]=game.moveMaskCachePoints;
            moveMaskFrames[index]=game.moveMaskCacheFrames;
            moveMaskHits[index]=game.moveMaskCacheHits;
            moveMaskMisses[index]=game.moveMaskCacheMisses;
            simulationUndoOperations[index]=search==null?0:search.simulationUndoOperations;
            wallMilliseconds[index]=(Time.realtimeSinceStartup-sampleStartedAt)*1000f;
            millisecondsPerVisit[index]=actualVisits[index]>0?
                wallMilliseconds[index]/actualVisits[index]:0f;
            superkoMilliseconds[index]=controller.lastSuperkoMilliseconds;
            featureMilliseconds[index]=controller.lastFeatureMilliseconds;
            mctsMilliseconds[index]=controller.lastSearchStepMilliseconds;
            mctsRootCopyMilliseconds[index]=controller.lastRootCopyMilliseconds;
            mctsSelectionMilliseconds[index]=controller.lastMctsSelectionMilliseconds;
            mctsExpansionMilliseconds[index]=controller.lastMctsExpansionMilliseconds;
            mctsBackupMilliseconds[index]=controller.lastMctsBackupMilliseconds;
            scoreUtilityMilliseconds[index]=controller.lastScoreUtilityMilliseconds;
            ladderMilliseconds[index]=controller.lastLadderMilliseconds;
            controllerMaxTickMilliseconds[index]=controller.maxTickMilliseconds;
            moveMaskMaxFrameMilliseconds[index]=game.maxMoveMaskFrameMilliseconds;
            featureSuperkoMilliseconds[index]=controller.lastFeatureSuperkoMilliseconds;
            featureClearMilliseconds[index]=controller.lastFeatureClearMilliseconds;
            featureBaseMilliseconds[index]=controller.lastFeatureBaseMilliseconds;
            featureAreaMilliseconds[index]=controller.lastFeatureAreaMilliseconds;
            inputPackingMilliseconds[index]=controller.lastGpuInputPackingMilliseconds;
            spatialApplyMilliseconds[index]=controller.lastGpuSpatialApplyMilliseconds;
            globalApplyMilliseconds[index]=controller.lastGpuGlobalApplyMilliseconds;
            inputBlitMilliseconds[index]=controller.lastGpuInputBlitMilliseconds;
            gpuSubmitMilliseconds[index]=controller.lastGpuDispatchMilliseconds;
            gpuSmoothedSubmitMilliseconds[index]=controller.smoothedGpuSubmitMilliseconds;
            gpuMaxSubmitMilliseconds[index]=controller.maxGpuSubmitMsOneFrame;
            gpuSubmitLastFrameMilliseconds[index]=controller.runtime==null?0f:
                controller.runtime.lastSubmitFrameMilliseconds;
            readbackMilliseconds[index]=controller.lastReadbackMilliseconds;
            uiRefreshMilliseconds[index]=game.ui==null?0f:game.ui.maxRefreshMilliseconds;
            CaptureFrameSamples(index);
            if(boardPool!=null)boardPool.StopFrameSamples();
            if(actualVisits[index]!=targetVisits[budgetIndex]||graphFrames[index]<2||
                readbackRequests[index]<=0)
            {Finish("performance corpus incomplete fixture="+fixtureIndex+" visits="+actualVisits[index]+" target="+targetVisits[budgetIndex]);return;}
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_CORPUS_SAMPLE fixture="+FixtureName(fixtureIndex)+
                " visits="+targetVisits[budgetIndex]+" wallMs="+wallMilliseconds[index].ToString("F3")+
                 " frames="+searchFrames[index]+" visitFrames="+
                 firstVisitCompletionFrames[visitFrameBase]+","+
                 firstVisitCompletionFrames[visitFrameBase+1]+","+
                 firstVisitCompletionFrames[visitFrameBase+2]+","+
                 firstVisitCompletionFrames[visitFrameBase+3]+" p50="+frameP50Milliseconds[index].ToString("F3")+
                " p95="+frameP95Milliseconds[index].ToString("F3")+" p99="+frameP99Milliseconds[index].ToString("F3")+
                " max="+frameMaxMilliseconds[index].ToString("F3")+" superkoMs="+superkoMilliseconds[index].ToString("F3")+
                " featureMs="+featureMilliseconds[index].ToString("F3")+" mctsMs="+mctsMilliseconds[index].ToString("F3")+
                " msPerVisit="+millisecondsPerVisit[index].ToString("F3")+
                " featureSteps="+featureSteps[index]+" ladderStepCalls="+ladderStepCalls[index]+
                " ladderTransitions="+ladderTransitions[index]+
                " ladderNodes="+ladderNodes[index]+" legalChecks="+legalMoveChecks[index]+
                " controllerMaxMs="+controllerMaxTickMilliseconds[index].ToString("F3")+
                " moveMask="+moveMaskPoints[index]+"/"+moveMaskFrames[index]+
                " moveMaskHits="+moveMaskHits[index]+" moveMaskMisses="+moveMaskMisses[index]+
                " moveMaskMaxMs="+moveMaskMaxFrameMilliseconds[index].ToString("F3")+
                " featureSuperkoMs="+featureSuperkoMilliseconds[index].ToString("F3")+
                " featureClearMs="+featureClearMilliseconds[index].ToString("F3")+
                " featureBaseMs="+featureBaseMilliseconds[index].ToString("F3")+
                " ladderMs="+ladderMilliseconds[index].ToString("F3")+
                " featureAreaMs="+featureAreaMilliseconds[index].ToString("F3")+
                " rootCopyMs="+mctsRootCopyMilliseconds[index].ToString("F3")+
                " selectionMs="+mctsSelectionMilliseconds[index].ToString("F3")+
                " expansionMs="+mctsExpansionMilliseconds[index].ToString("F3")+
                " backupMs="+mctsBackupMilliseconds[index].ToString("F3")+
                " scoreMs="+scoreUtilityMilliseconds[index].ToString("F3")+
                " scoreSamples="+scoreUtilitySamples[index]+
                " uploadPackMs="+inputPackingMilliseconds[index].ToString("F3")+
                " spatialApplyMs="+spatialApplyMilliseconds[index].ToString("F3")+
                " globalApplyMs="+globalApplyMilliseconds[index].ToString("F3")+
                " inputBlitMs="+inputBlitMilliseconds[index].ToString("F3")+
                " gpuMs="+gpuSubmitMilliseconds[index].ToString("F3")+" gpuSmoothMs="+gpuSmoothedSubmitMilliseconds[index].ToString("F3")+
                " gpuMaxMs="+gpuMaxSubmitMilliseconds[index].ToString("F3")+
                " gpuPasses="+gpuPasses[index]+" graphFrames="+graphFrames[index]+
                " readbacks="+readbackRequests[index]+" readbackMs="+readbackMilliseconds[index].ToString("F3")+
                 " uiMs="+uiRefreshMilliseconds[index].ToString("F3")+
                 " historyEntries="+historyEntriesExamined[index]+
                 " fixtureHash="+fixtureSequenceHashes[fixtureIndex]+
                 " rootCopiedInts="+rootStateCopiedInts[index]+
                " rootHistoryEntries="+rootHistoryCopiedEntries[index]+
                 " undoOps="+simulationUndoOperations[index]+
                 " sameFrameTransitions="+sameFramePhaseTransitions[index]+
                 " avoidableYields="+avoidableYields[index]+
                 " deadlineYields="+deadlineYields[index]+
                 " asyncReadbackWaitFrames="+asyncReadbackWaitFrames[index]+
                 " telemetryCommits="+telemetryCommitsDuringSearch[index]+
                 " telemetrySerializations="+telemetrySerializationsDuringSearch[index]+
                 " warmupSuppressedCriticalFrames="+moveMaskSuppressedCriticalFrames[index]+
                 " grantedCpuMs="+grantedCpuBudgetMilliseconds[index].ToString("F3")+
                 " usedCpuMs="+usedCpuBudgetMilliseconds[index].ToString("F3")+
                 " cpuOvershootMs="+maxCpuBudgetOvershootMilliseconds[index].ToString("F3")+
                 " gpuLastFrameMs="+gpuSubmitLastFrameMilliseconds[index].ToString("F3"));
            controller.autoStart=false;fixtureIndex++;
            if(fixtureIndex>=FIXTURE_COUNT)
            {
                fixtureIndex=0;budgetIndex++;
                if(budgetIndex>=BUDGET_COUNT){probePassed=true;probeFinished=true;phase=0;return;}
            }
            sampleIndex++;phase=PREPARE;
        }

        private int[] GetFixtureMoves(int index)
        {
            if(index==0)return fixtureAEmpty;
            if(index==1)return fixtureBOpening;
            if(index==2)return fixtureCTactical;
            if(index==3)return fixtureDDense;
            if(index==4)return fixtureELong;
            if(index==5)return fixtureFSuperko;
            if(index==6)return fixtureGLadder;
            if(index==7)return fixtureHEndgame;
            return fixtureAEmpty;
        }

        private void RecordFixtureMove(int move)
        {fixtureSequenceHash=fixtureSequenceHash*31+move+2;}

        private string FixtureName(int index)
        {
            if(index==0)return "A-empty";if(index==1)return "B-opening";
            if(index==2)return "C-tactical";if(index==3)return "D-dense";
            if(index==4)return "E-long";if(index==5)return "F-superko";
            if(index==6)return "G-ladder";return "H-endgame";
        }

        private void CaptureFrameSamples(int index)
        {
            if(boardPool==null||boardPool.frameMillisecondsSamples==null)return;
            int count=boardPool.frameSampleCount;
            if(count<=0)return;if(count>FRAME_SAMPLE_CAPACITY)count=FRAME_SAMPLE_CAPACITY;
            int write=boardPool.frameSampleWriteIndex;
            int start=count==boardPool.frameMillisecondsSamples.Length?write:0;
            frameSampleCounts[index]=count;
            int destination=index*FRAME_SAMPLE_CAPACITY;
            for(int i=0;i<count;i++)
                frameSamples[destination+i]=boardPool.frameMillisecondsSamples[
                    (start+i)%boardPool.frameMillisecondsSamples.Length];
            frameMaxMilliseconds[index]=boardPool.maxObservedFrameMilliseconds;
        }

        private void Finish(string message)
        {
            if(boardPool!=null)boardPool.StopFrameSamples();
            if(controller!=null)controller.autoStart=false;
            failure=message;probePassed=false;probeFinished=true;phase=0;
        }
    }
}
