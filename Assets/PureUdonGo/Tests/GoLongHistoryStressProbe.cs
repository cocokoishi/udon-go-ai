using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Deterministic 0/32/64/128/256/520-ply runtime stress corpus. Every case
    /// completes a 361-point legal/superko scan, cooperative feature encode,
    /// one real GPU evaluation, and an exact 32-visit PUCT move. A per-case
    /// 90-second budget remains a hard failure rather than being extended.
    /// </summary>
    public sealed class GoLongHistoryStressProbe : UdonSharpBehaviour
    {
        // Keep this stress gate at its historical 32-visit budget even when
        // the public Advanced preset is tuned independently.
        public const int SEARCH_VISITS = 32;
        private const int PREPARE=1,BUILD=2,MASK=3,ENCODE=4,SEARCH=5;
        private const float CASE_TIMEOUT_SECONDS=90f;
        public const int CASE_COUNT=6;
        public GoGame game;
        public GoAiController controller;
        public GoFeatureEncoder encoder;
        public GoAiSettings aiSettings;
        public int[] targetPlies={0,32,64,128,256,520};
        public int[] seeds={17,17391,48127,90211,137821,191939};
        public int firstCaseIndex;
        public int lastCaseExclusive=CASE_COUNT;
        public bool probeFinished;
        public bool probePassed;
        public int phase;
        public int caseIndex;
        public int generatedMoves;
        public int maskCursor;
        public int randomState;
        public bool previousWasPass;
        public bool fallbackSearchActive;
        public int fallbackSearchStart;
        public int fallbackSearchChecked;
        public bool exportBuildOnly;
        public bool buildOnlyFinished;
        public int exportedMoveCount;
        public int[] exportedPackedMoves=new int[GoGame.MAX_MOVES];
        public int[] fixedCase520Moves=new int[0];
        public bool useFixedCase520Moves=false;
        public bool useFixedCasePrefix=true;
        public int[] fixedCasePrefixMoves={
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
            329,360,226,296,9,45,162,136,158,14,263,90,187,64,95,8,160,154,20,192,73,180,11,
            184,258,207,202,293,119,236,182,232,141,304,153,156,116,65,283,200,208,142,174,299,
            354,59,171,43,28,336,70,191,32,144,150,165,302,41,328,159,230,109,139,93,52,118,63,
            146,249,131,112,74,325,56,231,351,107,327,301,280,15,220,330,88,106,339,239,163,78,
            30,61,115,161,148,135,240,338,246,343,291,133,126,76,190,1,196,49,255,26,22,189,155,
            103,212,46,261,111,85,272,253,337,355,257,104,0,166,21,178,81,177,245,347,36,314,270,
            242,206,123,198,130,267,121,129,122,134,114,254,72,211,322,294,308,38,170,60,313,260,
            252,310,358,323,292,167,113,67,278,53,227,221,289,51,37,58,87,210,75,286,324,305,268,
            279,132,172,335,128,39,315,42,91,271,80,35,24,350,68,217,33,86,277,307,138,105,99,275,
            359,40,110,273,218,201,251,312,125,344,62,250,332,117,124,100,213,306,284,168,269,183,
            353,235,309,84,102,151,97,34,282,108,47,31,234,254,13,244,266,349,3,44,285,311,346,205,
            69,288,5,295,199,274,194,272,12,4,173,222,215,204,71,209,233,54,303,316,193,223,89,326,
            356,298,348,79,55,101,238,352,300,334,57,224,82,179,25,195,23,276,181,243,96,248,186,214,
            287,228,219,225,247,340,176,127,232,220,229,149,209,83,103,290,203,164,241,27,331,324,48,
            262,342,145,4,357,77,317,192,216,143,264,250,304,157,66,18,169,265,297,245,50,16,197,152,
            115,201,237,175,140,10,7,102,147,190,114,103,319,358,29,29,6,120,27,333,360,248,6,228,
            343,98,226,30,352,357,323,7,31,191,315,347,17,268,320,354,256,220,188,92,208,6,150,318,
            325,35,18,281,338,54,321,19,189,337,353,259,8,200,185,102,137,156,155,101,318,264,283,
            136,94,100,294,284,345,331,301,347,309,281,302,114,265,333,328,332,206,348,337,212,346,
            330,137,300,117,240,239,264,124,2,107,199,257,202,260,154,263,187,167,27,218,259,160,
            143,279,118,241,277,240,181,161,8,341,219,245,106,354,200,203,125,198,125,50,72,329,333,
            282,332,258,162,31,137,300,330,182,155,359,356,342,358,310,115,220,34,238,50,259,221,348,
            201,221,181,106,143,202,79,357,331,281,31,141,162,330};

        public int baselineMoveCount;
        public int baselineAiMoves;
        public float caseStartedAt;
        public float phaseStartedAt;
        public int[] completedPlies=new int[CASE_COUNT];
        public int[] legalCounts=new int[CASE_COUNT];
        public int[] superkoBannedCounts=new int[CASE_COUNT];
        public int[] occupiedCounts=new int[CASE_COUNT];
        public int[] captureEvents=new int[CASE_COUNT];
        public int[] capturedStones=new int[CASE_COUNT];
        public int[] illegalEmptyCandidates=new int[CASE_COUNT];
        public int[] koObservations=new int[CASE_COUNT];
        public int[] featureGenerations=new int[CASE_COUNT];
        public float[] featureChecksums=new float[CASE_COUNT];
        public int[] searchVisits=new int[CASE_COUNT];
        public int[] searchFrames=new int[CASE_COUNT];
        public float[] searchMilliseconds=new float[CASE_COUNT];
        // Search-stage evidence is captured only once per completed case, so
        // the profiler adds no per-visit allocations or string work.
        public float[] searchFeatureMilliseconds=new float[CASE_COUNT];
        public float[] searchFeatureActiveCpuMilliseconds=new float[CASE_COUNT];
        public float[] searchLadderActiveCpuMilliseconds=new float[CASE_COUNT];
        public float[] searchGpuUploadMilliseconds=new float[CASE_COUNT];
        public float[] searchGpuDispatchMilliseconds=new float[CASE_COUNT];
        public float[] searchReadbackMilliseconds=new float[CASE_COUNT];
        public float[] searchMctsSelectionMilliseconds=new float[CASE_COUNT];
        public float[] searchMctsExpansionMilliseconds=new float[CASE_COUNT];
        public float[] searchMctsBackupMilliseconds=new float[CASE_COUNT];
        public float[] searchRootCopyMilliseconds=new float[CASE_COUNT];
        public float[] searchScoreUtilityMilliseconds=new float[CASE_COUNT];
        public float[] searchTickMaxMilliseconds=new float[CASE_COUNT];
        public float[] buildMilliseconds=new float[CASE_COUNT];
        public float[] maskMilliseconds=new float[CASE_COUNT];
        public float[] featureMilliseconds=new float[CASE_COUNT];
        public float[] featureSuperkoMilliseconds=new float[CASE_COUNT];
        public float[] featureClearMilliseconds=new float[CASE_COUNT];
        public float[] featureBaseMilliseconds=new float[CASE_COUNT];
        public float[] featureLadderMilliseconds=new float[CASE_COUNT];
        public float[] featureAreaMilliseconds=new float[CASE_COUNT];
        public float[] featureActiveCpuMilliseconds=new float[CASE_COUNT];
        public float[] featureLadderActiveCpuMilliseconds=new float[CASE_COUNT];
        public int[] featureSteps=new int[CASE_COUNT];
        public int[] ladderTransitions=new int[CASE_COUNT];
        public int[] ladderNodes=new int[CASE_COUNT];
        public int[] ladderDfsCalls=new int[CASE_COUNT];
        public int[] maxLadderTransitionsOneSlice=new int[CASE_COUNT];
        public int[] ladderCacheLookups=new int[CASE_COUNT];
        public int[] ladderCacheEntriesChecked=new int[CASE_COUNT];
        public int[] ladderCacheKeyComparisons=new int[CASE_COUNT];
        public int[] ladderCacheFullHits=new int[CASE_COUNT];
        public int[] ladderCacheMarkerHits=new int[CASE_COUNT];
        public int[] ladderCacheWorkingOnlyHits=new int[CASE_COUNT];
        public int[] historyQueryCounts=new int[CASE_COUNT];
        public int[] historyBucketHits=new int[CASE_COUNT];
        public int[] historyBucketMisses=new int[CASE_COUNT];
        public int[] historyFullScans=new int[CASE_COUNT];
        public int[] historyMaxEntriesExamined=new int[CASE_COUNT];
        public int[] liveSearchPhase=new int[CASE_COUNT];
        public int[] liveSearchVisits=new int[CASE_COUNT];
        public int[] liveSearchStepCalls=new int[CASE_COUNT];
        public int[] liveLegalMoveChecks=new int[CASE_COUNT];
        public int[] liveSuperkoPoints=new int[CASE_COUNT];
        public int[] liveSuperkoFrames=new int[CASE_COUNT];
        public int[] liveGpuPasses=new int[CASE_COUNT];
        public int[] liveGpuGraphFrames=new int[CASE_COUNT];
        public int[] liveHistoryQueries=new int[CASE_COUNT];
        public int[] liveHistoryBucketHits=new int[CASE_COUNT];
        public int[] liveHistoryBucketMisses=new int[CASE_COUNT];
        public int[] liveHistoryFullScans=new int[CASE_COUNT];
        public int[] liveHistoryMaxEntries=new int[CASE_COUNT];
        public int[] liveEncoderState=new int[CASE_COUNT];
        public int[] liveEncoderSteps=new int[CASE_COUNT];
        public int[] liveEncoderLadderTransitions=new int[CASE_COUNT];
        public int[] liveEncoderLadderNodes=new int[CASE_COUNT];
        public int[] liveEncoderCacheHits=new int[CASE_COUNT];
        public int[] liveEncoderCacheMisses=new int[CASE_COUNT];
        public float[] liveFeatureMilliseconds=new float[CASE_COUNT];
        public float[] liveLadderMilliseconds=new float[CASE_COUNT];
        public float[] liveSuperkoMilliseconds=new float[CASE_COUNT];
        public float[] liveGpuUploadMilliseconds=new float[CASE_COUNT];
        public float[] liveGpuDispatchMilliseconds=new float[CASE_COUNT];
        public float[] liveReadbackMilliseconds=new float[CASE_COUNT];
        public float[] liveMctsMilliseconds=new float[CASE_COUNT];
        public int[] neuralPasses=new int[CASE_COUNT];
        public int[] gpuGraphFrames=new int[CASE_COUNT];
        public int[] historyEntriesExamined=new int[CASE_COUNT];
        public string failure="";

        public void RunLongHistoryStressProbe()
        {
            probeFinished=false;probePassed=false;phase=PREPARE;
            buildOnlyFinished=false;exportedMoveCount=0;
            firstCaseIndex=Mathf.Clamp(firstCaseIndex,0,CASE_COUNT-1);
            lastCaseExclusive=Mathf.Clamp(lastCaseExclusive,firstCaseIndex+1,CASE_COUNT);
            caseIndex=firstCaseIndex;
            failure="";
            if(game==null||controller==null||encoder==null||
                targetPlies==null||targetPlies.Length!=CASE_COUNT||
                seeds==null||seeds.Length!=CASE_COUNT)
            {Finish("long-history stress references are incomplete");return;}
            for(int i=0;i<CASE_COUNT;i++)
            {
                historyEntriesExamined[i]=0;historyQueryCounts[i]=0;
                historyBucketHits[i]=0;historyBucketMisses[i]=0;
                historyFullScans[i]=0;historyMaxEntriesExamined[i]=0;
            }
            controller.autoStart=false;controller.InvalidateSearch();
        }

        public void Update()
        {
            if(probeFinished||phase==0)return;
            if(caseStartedAt>0f&&Time.realtimeSinceStartup-caseStartedAt>
                CASE_TIMEOUT_SECONDS)
            {CaptureLiveTelemetry();Finish("case timeout index="+caseIndex+" target="+
                targetPlies[caseIndex]+" phase="+phase+" liveSearchPhase="+
                liveSearchPhase[caseIndex]+" liveVisits="+liveSearchVisits[caseIndex]);return;}
            if(phase==PREPARE)
            {
                controller.autoStart=false;controller.InvalidateSearch();
                game.positionalSuperko=true;game.multiStoneSuicideLegal=true;
                game.areaScoring=true;game.SetMatchMode(GoAiController.MODE_AIVAI);
                GoAiSettings settings=aiSettings==null?game.aiSettings:aiSettings;
                if(settings!=null)
                {
                    settings.ApplyPackedVisits(GoDifficultyProfile.ADVANCED,
                        SEARCH_VISITS, SEARCH_VISITS);
                }
                else
                {
                    if(game.blackDifficulty!=null)game.blackDifficulty.ApplyResolvedLocal(
                        GoDifficultyProfile.ADVANCED,SEARCH_VISITS,0,true);
                    if(game.whiteDifficulty!=null)game.whiteDifficulty.ApplyResolvedLocal(
                        GoDifficultyProfile.ADVANCED,SEARCH_VISITS,0,true);
                }
                game.RequestForceReset();game.SetBlackStarts();game.RequestStartMatch();
                generatedMoves=0;maskCursor=0;randomState=seeds[caseIndex];
                previousWasPass=false;fallbackSearchActive=false;fallbackSearchStart=0;
                fallbackSearchChecked=0;caseStartedAt=Time.realtimeSinceStartup;
                phaseStartedAt=caseStartedAt;
                phase=BUILD;return;
            }
            if(phase==BUILD)
            {
                if(generatedMoves>=targetPlies[caseIndex])
                {
                    completedPlies[caseIndex]=game.moveCount;
                    if(exportBuildOnly)
                    {
                        exportedMoveCount=game.moveCount;
                        for(int i=0;i<exportedPackedMoves.Length;i++)
                            exportedPackedMoves[i]=i<game.moveHistory.Length?game.moveHistory[i]:0;
                        buildOnlyFinished=true;probePassed=true;probeFinished=true;phase=0;return;
                    }
                    int occupied=0;
                    for(int i=0;i<GoGame.AREA;i++)if(game.board[i]!=GoGame.EMPTY)occupied++;
                    occupiedCounts[caseIndex]=occupied;
                    buildMilliseconds[caseIndex]=(Time.realtimeSinceStartup-phaseStartedAt)*1000f;
                    maskCursor=0;phaseStartedAt=Time.realtimeSinceStartup;phase=MASK;return;
                }
                if(caseIndex!=5&&(!useFixedCase520Moves||fixedCase520Moves==null||
                    fixedCase520Moves.Length<targetPlies[caseIndex])&&
                    !previousWasPass&&generatedMoves>0&&
                    (generatedMoves+caseIndex*11)%67==0)
                {
                    int before=game.moveCount;game.PassFromAI();
                    if(game.moveCount!=before+1)
                    {Finish("deterministic pass was rejected case="+caseIndex);return;}
                    previousWasPass=true;generatedMoves++;return;
                }
                if(caseIndex==5&&useFixedCasePrefix&&fixedCasePrefixMoves!=null&&
                    generatedMoves<fixedCasePrefixMoves.Length)
                {
                    int prefixLoc=fixedCasePrefixMoves[generatedMoves];
                    int beforePrefixCaptures=game.blackCaptures+game.whiteCaptures;
                    if(!game.TryPlayFromAI(prefixLoc))
                    {Finish("frozen case520 prefix move rejected index="+generatedMoves+" loc="+prefixLoc);return;}
                    int prefixDelta=game.blackCaptures+game.whiteCaptures-beforePrefixCaptures;
                    if(prefixDelta>0){captureEvents[caseIndex]++;capturedStones[caseIndex]+=prefixDelta;}
                    if(game.koLoc!=GoGame.NONE)koObservations[caseIndex]++;
                    previousWasPass=false;generatedMoves++;return;
                }
                if(caseIndex==5&&useFixedCase520Moves&&fixedCase520Moves!=null&&
                    fixedCase520Moves.Length>=targetPlies[caseIndex])
                {
                    int fixedLoc=fixedCase520Moves[generatedMoves];
                    int beforeFixedCaptures=game.blackCaptures+game.whiteCaptures;
                    int beforeFixedMoveCount=game.moveCount;
                    bool fixedAccepted=false;
                    if(fixedLoc==GoGame.PASS)
                    {game.PassFromAI();fixedAccepted=game.moveCount==beforeFixedMoveCount+1;}
                    else fixedAccepted=game.TryPlayFromAI(fixedLoc);
                    if(!fixedAccepted)
                    {Finish("frozen case520 move rejected index="+generatedMoves+" loc="+fixedLoc);return;}
                    int fixedDelta=game.blackCaptures+game.whiteCaptures-beforeFixedCaptures;
                    if(fixedDelta>0){captureEvents[caseIndex]++;capturedStones[caseIndex]+=fixedDelta;}
                    if(game.koLoc!=GoGame.NONE)koObservations[caseIndex]++;
                    previousWasPass=fixedLoc==GoGame.PASS;generatedMoves++;return;
                }
                int beforeCaptures=game.blackCaptures+game.whiteCaptures;
                bool accepted=false;int chosenLocation=GoGame.NONE;
                if(fallbackSearchActive)
                {
                    int checkedThisFrame=0;
                    while(fallbackSearchChecked<GoGame.AREA&&checkedThisFrame<48)
                    {
                        int loc=(fallbackSearchStart+fallbackSearchChecked)%GoGame.AREA;
                        fallbackSearchChecked++;checkedThisFrame++;
                        bool legal=game.IsRulesLegalMove(loc);
                        if(game.board[loc]==GoGame.EMPTY&&!legal)
                            illegalEmptyCandidates[caseIndex]++;
                        if(!legal)continue;
                        if(game.TryPlayFromAI(loc)){accepted=true;chosenLocation=loc;break;}
                    }
                    if(!accepted)
                    {
                        if(fallbackSearchChecked<GoGame.AREA)return;
                        fallbackSearchActive=false;
                        Finish("deterministic random corpus exhausted all legal points case="+
                            caseIndex+" generated="+generatedMoves);
                        return;
                    }
                    fallbackSearchActive=false;
                }
                else
                {
                    for(int attempt=0;attempt<64;attempt++)
                    {
                        int loc=NextRandom()%GoGame.AREA;
                        bool legal=game.IsRulesLegalMove(loc);
                        if(game.board[loc]==GoGame.EMPTY&&!legal)
                            illegalEmptyCandidates[caseIndex]++;
                        if(!legal)continue;
                        accepted=game.TryPlayFromAI(loc);
                        if(accepted){chosenLocation=loc;break;}
                    }
                    if(!accepted)
                    {
                        // The random stream remains deterministic, but a
                        // dense/superko position can legitimately reject all
                        // 64 samples. Continue from a deterministic board
                        // location over subsequent frames instead of turning
                        // a corpus construction miss into a false search fail.
                        fallbackSearchActive=true;
                        fallbackSearchStart=NextRandom()%GoGame.AREA;
                        fallbackSearchChecked=0;
                        return;
                    }
                }
                int delta=game.blackCaptures+game.whiteCaptures-beforeCaptures;
                if(delta>0){captureEvents[caseIndex]++;capturedStones[caseIndex]+=delta;}
                if(game.koLoc!=GoGame.NONE)koObservations[caseIndex]++;
                previousWasPass=false;generatedMoves++;return;
            }
            if(phase==MASK)
            {
                int end=maskCursor+16;if(end>GoGame.AREA)end=GoGame.AREA;
                while(maskCursor<end)
                {
                    int mask=game.GetMoveMask(maskCursor);
                    if((mask&1)!=0)legalCounts[caseIndex]++;
                    if((mask&2)!=0)superkoBannedCounts[caseIndex]++;
                    maskCursor++;
                }
                if(maskCursor<GoGame.AREA)return;
                maskMilliseconds[caseIndex]=(Time.realtimeSinceStartup-phaseStartedAt)*1000f;
                phaseStartedAt=Time.realtimeSinceStartup;
                if(!encoder.BeginEncodeGame(game))
                {Finish("feature encode did not start case="+caseIndex);return;}
                phase=ENCODE;return;
            }
            if(phase==ENCODE)
            {
                int state=encoder.StepEncode(64);
                if(state==GoFeatureEncoder.ENCODE_RUNNING)return;
                if(state!=GoFeatureEncoder.ENCODE_COMPLETE)
                {Finish("feature encode failed case="+caseIndex+" "+encoder.lastError);return;}
                float checksum=0f;
                for(int i=0;i<GoFeatureEncoder.SPATIAL_COUNT;i+=17)
                    checksum+=encoder.spatialOutput[i]*(i+1);
                for(int i=0;i<GoFeatureEncoder.GLOBAL_CHANNELS;i++)
                    checksum+=encoder.globalOutput[i]*(i+1);
                featureChecksums[caseIndex]=checksum;
                featureGenerations[caseIndex]=encoder.encodeGeneration;
                featureMilliseconds[caseIndex]=(Time.realtimeSinceStartup-phaseStartedAt)*1000f;
                featureSuperkoMilliseconds[caseIndex]=encoder.lastScanSuperkoMilliseconds;
                featureClearMilliseconds[caseIndex]=encoder.lastClearSpatialMilliseconds;
                featureBaseMilliseconds[caseIndex]=encoder.lastBaseMilliseconds;
                featureLadderMilliseconds[caseIndex]=encoder.lastLadderMilliseconds;
                featureAreaMilliseconds[caseIndex]=encoder.lastAreaMilliseconds;
                featureActiveCpuMilliseconds[caseIndex]=encoder.featureActiveCpuMilliseconds;
                featureLadderActiveCpuMilliseconds[caseIndex]=encoder.ladderActiveCpuMilliseconds;
                featureSteps[caseIndex]=encoder.lastEncodeSteps;
                ladderTransitions[caseIndex]=encoder.lastLadderSearchTransitions;
                ladderNodes[caseIndex]=encoder.lastLadderNodes;
                ladderDfsCalls[caseIndex]=encoder.ladderDfsCalls;
                maxLadderTransitionsOneSlice[caseIndex]=encoder.maxLadderTransitionsOneSlice;
                ladderCacheLookups[caseIndex]=encoder.ladderCacheLookups;
                ladderCacheEntriesChecked[caseIndex]=encoder.ladderCacheEntriesChecked;
                ladderCacheKeyComparisons[caseIndex]=encoder.ladderCacheKeyComparisons;
                ladderCacheFullHits[caseIndex]=encoder.ladderCacheFullHits;
                ladderCacheMarkerHits[caseIndex]=encoder.ladderCacheMarkerHits;
                ladderCacheWorkingOnlyHits[caseIndex]=encoder.ladderCacheWorkingOnlyHits;
                baselineMoveCount=game.moveCount;baselineAiMoves=controller.aiMoves;
                controller.autoStart=true;phase=SEARCH;return;
            }
            if(controller.controllerState==GoAiController.STATE_ERROR)
            {Finish("search error case="+caseIndex+" "+controller.lastError);return;}
            if(game.moveCount<=baselineMoveCount||controller.aiMoves<=baselineAiMoves)return;
            searchVisits[caseIndex]=controller.lastSearchCompletedVisits;
            historyEntriesExamined[caseIndex]=controller.lastHistoryEntriesExamined;
            historyQueryCounts[caseIndex]=controller.lastHistoryQueryCount;
            historyBucketHits[caseIndex]=controller.lastHistoryBucketHitCount;
            historyBucketMisses[caseIndex]=controller.lastHistoryBucketMissCount;
            historyFullScans[caseIndex]=controller.lastHistoryFullScanCount;
            historyMaxEntriesExamined[caseIndex]=controller.lastHistoryMaxEntriesExamined;
            searchFrames[caseIndex]=controller.lastSearchFrames;
            searchMilliseconds[caseIndex]=controller.lastSearchMilliseconds;
            searchFeatureMilliseconds[caseIndex]=controller.lastFeatureMilliseconds;
            searchFeatureActiveCpuMilliseconds[caseIndex]=controller.lastFeatureActiveCpuMilliseconds;
            searchLadderActiveCpuMilliseconds[caseIndex]=controller.lastLadderActiveCpuMilliseconds;
            searchGpuUploadMilliseconds[caseIndex]=controller.lastGpuUploadMilliseconds;
            searchGpuDispatchMilliseconds[caseIndex]=controller.lastGpuDispatchMilliseconds;
            searchReadbackMilliseconds[caseIndex]=controller.lastReadbackMilliseconds;
            searchMctsSelectionMilliseconds[caseIndex]=controller.lastMctsSelectionMilliseconds;
            searchMctsExpansionMilliseconds[caseIndex]=controller.lastMctsExpansionMilliseconds;
            searchMctsBackupMilliseconds[caseIndex]=controller.lastMctsBackupMilliseconds;
            searchRootCopyMilliseconds[caseIndex]=controller.lastRootCopyMilliseconds;
            searchScoreUtilityMilliseconds[caseIndex]=controller.lastScoreUtilityMilliseconds;
            searchTickMaxMilliseconds[caseIndex]=controller.lastTickMilliseconds;
            neuralPasses[caseIndex]=controller.lastGpuPasses;
            gpuGraphFrames[caseIndex]=controller.lastGpuGraphFrames;
            if(searchVisits[caseIndex]!=SEARCH_VISITS||
                neuralPasses[caseIndex]<GoGpuNeuralRuntime.FUSED_PASSES_WITHOUT_OWNERSHIP||
                gpuGraphFrames[caseIndex]<2)
            {Finish("incomplete search case="+caseIndex+" visits="+searchVisits[caseIndex]);return;}
            controller.autoStart=false;caseIndex++;
            if(caseIndex>=lastCaseExclusive)
            {
                int totalCaptures=0,totalIllegalEmpty=0;
                for(int i=firstCaseIndex;i<lastCaseExclusive;i++)
                {totalCaptures+=captureEvents[i];totalIllegalEmpty+=illegalEmptyCandidates[i];}
                if(firstCaseIndex==0&&lastCaseExclusive==CASE_COUNT&&
                    (totalCaptures<=0||totalIllegalEmpty<=0))
                {Finish("random corpus missed capture/suicide-like pressure");return;}
                probePassed=true;probeFinished=true;phase=0;return;
            }
            phase=PREPARE;caseStartedAt=0f;
        }

        private int NextRandom()
        {randomState=randomState*1103515245+12345;return randomState&0x7fffffff;}
        private void CaptureLiveTelemetry()
        {
            if(caseIndex<0||caseIndex>=CASE_COUNT)return;
            if(controller!=null)
            {
                liveSuperkoPoints[caseIndex]=controller.superkoProbePoints;
                liveSuperkoFrames[caseIndex]=controller.superkoProbeFrames;
                liveGpuPasses[caseIndex]=controller.lastGpuPasses;
                liveGpuGraphFrames[caseIndex]=controller.lastGpuGraphFrames;
                liveFeatureMilliseconds[caseIndex]=controller.lastFeatureMilliseconds;
                liveLadderMilliseconds[caseIndex]=controller.lastLadderMilliseconds;
                liveSuperkoMilliseconds[caseIndex]=controller.lastSuperkoMilliseconds;
                liveGpuUploadMilliseconds[caseIndex]=controller.lastGpuUploadMilliseconds;
                liveGpuDispatchMilliseconds[caseIndex]=controller.lastGpuDispatchMilliseconds;
                liveReadbackMilliseconds[caseIndex]=controller.lastReadbackMilliseconds;
                liveMctsMilliseconds[caseIndex]=controller.lastSearchStepMilliseconds;
                if(controller.search!=null)
                {
                    liveSearchPhase[caseIndex]=controller.search.phase;
                    liveSearchVisits[caseIndex]=controller.search.visitsCompleted;
                    liveSearchStepCalls[caseIndex]=controller.search.searchStepCalls;
                    liveLegalMoveChecks[caseIndex]=controller.search.legalMoveChecks;
                    if(controller.search.simulationState!=null)
                    {
                        liveHistoryQueries[caseIndex]=controller.search.simulationState.historyQueryCount;
                        liveHistoryBucketHits[caseIndex]=controller.search.simulationState.historyBucketHitCount;
                        liveHistoryBucketMisses[caseIndex]=controller.search.simulationState.historyBucketMissCount;
                        liveHistoryFullScans[caseIndex]=controller.search.simulationState.historyFullScanCount;
                        liveHistoryMaxEntries[caseIndex]=controller.search.simulationState.historyMaxEntriesExamined;
                    }
                }
            }
            if(encoder!=null)
            {
                liveEncoderState[caseIndex]=encoder.encodeState;
                liveEncoderSteps[caseIndex]=encoder.lastEncodeSteps;
                liveEncoderLadderTransitions[caseIndex]=encoder.encodeLadderTransitions;
                liveEncoderLadderNodes[caseIndex]=encoder.lastLadderNodes;
                liveEncoderCacheHits[caseIndex]=encoder.lastLadderCacheHits;
                liveEncoderCacheMisses[caseIndex]=encoder.lastLadderCacheMisses;
            }
        }
        private void Finish(string message)
        {if(controller!=null)controller.autoStart=false;failure=message;phase=0;probeFinished=true;}
    }
}
