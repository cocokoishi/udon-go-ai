using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// KataGo INPUTSVERSION 7 encoder for the fixed 19x19 production model.
    /// The plane order and global inputs follow the bundled
    /// .KataGO/KataGo-master/cpp/neuralnet/nninputs.cpp implementation.
    /// </summary>
    public sealed class GoFeatureEncoder : UdonSharpBehaviour
    {
        public const int SPATIAL_CHANNELS=22, GLOBAL_CHANNELS=19, SPATIAL_COUNT=SPATIAL_CHANNELS*GoGame.AREA;
        private const int MAX_LADDER_DEPTH=(GoGame.AREA*3)/2+1, LADDER_NODE_BUDGET=25000;

        public float[] spatialOutput=new float[SPATIAL_COUNT];
        public float[] globalOutput=new float[GLOBAL_CHANNELS];
        public string lastError="";

        public const int ENCODE_IDLE=0, ENCODE_RUNNING=1, ENCODE_COMPLETE=2,
            ENCODE_ERROR=3, ENCODE_CANCELLED=4;
        public int encodeState=ENCODE_IDLE;
        public int encodeStage;
        public int encodeCursor;
        public int encodeLadderPlane;
        public int encodeLadderTransitions;
        // Increments for every cooperative runtime feature request. It is
        // intentionally public so ClientSim can prove that every MCTS leaf
        // gets a fresh feature encoding rather than a reused completed buffer.
        public int encodeGeneration;
        public float lastEncodeMilliseconds;
        public float lastScanSuperkoMilliseconds;
        public float lastClearSpatialMilliseconds;
        // Cold-path evidence: the owned output is zero-initialized by the
        // runtime, so its first encode must not clear 7942 floats again.
        public int lastSpatialClearCount;
        public int lastSpatialClearSlices;
        public bool lastEncodeUsedFreshOwnedSpatial;
        public float lastBaseMilliseconds;
        public float lastLadderMilliseconds;
        public float lastAreaMilliseconds;
        public int lastEncodeSteps;
        public int lastLadderSearchTransitions;
        public int lastLadderNodes;
        public int lastLadderCacheHits;
        public int lastLadderCacheMisses;
        public float lastEncodeStepMilliseconds;
        public float maxEncodeStepMilliseconds;
        // Low-overhead ladder profiler counters. These are accumulated for one
        // feature encode and intentionally avoid per-transition allocations or
        // strings so the counters themselves cannot become the hot path.
        public float featureActiveCpuMilliseconds;
        public float ladderActiveCpuMilliseconds;
        public int ladderSliceCalls;
        public int ladderDfsCalls;
        public int maxLadderTransitionsOneSlice;
        public float maxLadderSliceMilliseconds;
        public int ladderGroupProbeCalls;
        public int ladderGroupPointsVisited;
        public int ladderCacheLookups;
        public int ladderCacheEntriesChecked;
        public int ladderCacheKeyComparisons;
        public int ladderCacheFullHits;
        public int ladderCacheMarkerHits;
        public int ladderCacheWorkingOnlyHits;
        public int ladderWorkConsumedThisCall;
        private int stepLadderTransitions;
        private int stepLadderSlices;
        private float stepLadderCpuMilliseconds;
        public int diagnosticLadderStart;
        public int diagnosticLadderStage;
        public int diagnosticLadderDepth;
        public int diagnosticLadderNodes;
        public int diagnosticLadderTransitions;
        public int diagnosticLadderPlane;
        public bool diagnosticLadderSearchActive;
        public bool ladderStorageAllocated;

        private int[] activeBoard;
        private int[] groupQueue=new int[GoGame.AREA];
        private int[] groupMembers=new int[GoGame.AREA];
        // Ladder DFS keeps the target group alive while capture probes inspect
        // neighboring groups. These fixed buffers avoid copying a target group
        // into a second array on every DFS node without using a generic array
        // parameter in the Udon hot path.
        private int[] captureGroupMembers=new int[GoGame.AREA];
        private int[] captureLibertyList=new int[GoGame.AREA];
        private int[] savedTargetGroup=new int[GoGame.AREA];
        private int[] leftNeighbor=new int[GoGame.AREA];
        private int[] rightNeighbor=new int[GoGame.AREA];
        private int[] upNeighbor=new int[GoGame.AREA];
        private int[] downNeighbor=new int[GoGame.AREA];
        private bool neighborTablesReady;
        private int[] generatedTargetLiberties=new int[GoGame.AREA];
        private int generatedTargetLibertyCount;
        private int[] ladderCaptureProbeStamp=new int[GoGame.AREA];
        private int[] ladderCaptureProbeMove=new int[GoGame.AREA];
        private int ladderCaptureProbeGeneration=1;
        private int[] ladderMoveCaptureStamp=new int[GoGame.AREA];
        private int ladderMoveCaptureGeneration=1;
        private int[] markedGroup=new int[GoGame.AREA];
        // Group probes are executed repeatedly by every ladder transition.
        // Generation stamps preserve the old board-order semantics without
        // clearing two full 19x19 marker arrays for every probe.
        private int[] visitedStamp=new int[GoGame.AREA];
        private int[] libertyStamp=new int[GoGame.AREA];
        private int groupStamp=1;
        private int currentLibertyCount;
        private int[] groupProcessedStamp=new int[GoGame.AREA];
        private int groupProcessedGeneration=1;
        // Base stone/liberty planes use a generation stamp so each feature
        // leaf does not synchronously clear another 361-element marker array.
        // Ladder stages keep their own legacy bool marker because their scan
        // is already resumed cooperatively.
        private int[] baseGroupProcessedStamp=new int[GoGame.AREA];
        private int baseGroupStamp=1;
        private bool[] areaVisited=new bool[GoGame.AREA];
        private bool[] bannedScratch=new bool[GoGame.AREA];
        private int[] areaOwner=new int[GoGame.AREA];
        private int[] libertyList=new int[GoGame.AREA];

        private int[] ladderBoard=new int[GoGame.AREA];
        // One journal restores an entire ladder attempt. Nested DFS snapshots
        // remain branch-local, while the attempt journal removes the old
        // per-candidate full-board restore.
        private int[] ladderChangedStamp=new int[GoGame.AREA];
        private int[] ladderChangedLocations=new int[GoGame.AREA];
        private int[] ladderChangedOriginal=new int[GoGame.AREA];
        private int ladderChangeGeneration=1;
        private int ladderChangedCount;
        private bool ladderAttemptActive;
        // These two buffers dominate the encoder's per-table memory. Hidden
        // pool entries allocate them only when their first feature request
        // begins; the fixed ladder depth/capacity remains unchanged.
        private int[] ladderSnapshots;
        private int[] ladderSnapshotChangedCount=new int[MAX_LADDER_DEPTH];
        private int[] ladderSnapshotStamp=new int[MAX_LADDER_DEPTH];
        private int[] ladderSnapshotMark=new int[GoGame.AREA];
        private int ladderSnapshotSerial=1;
        private int ladderSnapshotActiveDepth=-1;
        private int[] ladderMoveBuffer;
        private int[] ladderMoveCur=new int[MAX_LADDER_DEPTH];
        private int[] ladderMoveLen=new int[MAX_LADDER_DEPTH];
        private int[] ladderKoAtDepth=new int[MAX_LADDER_DEPTH];
        private int[] ladderWorkingMoves=new int[GoGame.AREA];
        private int[] ladderInitialLiberties=new int[GoGame.AREA];
        private int[] ladderTrialChangedLoc=new int[GoGame.AREA];
        private int[] ladderTrialOldValue=new int[GoGame.AREA];
        private bool[] ladderTrialMarked=new bool[GoGame.AREA];
        private int ladderTrialChangedCount;
        private bool ladderTrialActive;
        private int ladderKoLoc, ladderTargetColor, ladderNodeCount, ladderTargetGroupSize;
        private const int LADDER_CACHE_SET_COUNT=32, LADDER_CACHE_WAYS=4,
            LADDER_CACHE_ENTRIES=LADDER_CACHE_SET_COUNT*LADDER_CACHE_WAYS,
            LADDER_CACHE_KEY_WORDS=24, LADDER_CACHE_MASK_WORDS=(GoGame.AREA+31)/32;
        private bool[] ladderCacheValid=new bool[LADDER_CACHE_ENTRIES];
        private bool[] ladderCacheHasWorking=new bool[LADDER_CACHE_ENTRIES];
        private int[] ladderCacheSide=new int[LADDER_CACHE_ENTRIES];
        // Exact cache key: two bits per board point, padded to 24 ints. The
        // set hash only chooses four candidate ways; equality always compares
        // every key word, so collisions cannot produce a false hit.
        private int[] ladderCacheKey=new int[LADDER_CACHE_ENTRIES*LADDER_CACHE_KEY_WORDS];
        private int[] ladderCacheMaskPacked=new int[LADDER_CACHE_ENTRIES*LADDER_CACHE_MASK_WORDS];
        private int[] ladderCacheWorkingPacked=new int[LADDER_CACHE_ENTRIES*LADDER_CACHE_MASK_WORDS];
        private int[] ladderCacheQueryKey=new int[LADDER_CACHE_KEY_WORDS];
        private int[] ladderCacheSetNextWay=new int[LADDER_CACHE_SET_COUNT];
        private int ladderCacheQuerySet;
        private int asyncLadderCacheEntry=-1;
        private int asyncLadderCacheCursor;
        private bool asyncLadderCacheHit;
        private bool asyncLadderWorkingOnly;

        private const int ASYNC_STAGE_SCAN_SUPERKO=0, ASYNC_STAGE_CLEAR_SPATIAL=1,
            ASYNC_STAGE_BASE=2, ASYNC_STAGE_LADDER=3, ASYNC_STAGE_AREA=4;
        private const int ASYNC_LADDER_SCAN=0, ASYNC_LADDER_RUN_SINGLE=1,
            ASYNC_LADDER_PREPARE_TWO=2, ASYNC_LADDER_INIT_TWO=3,
            ASYNC_LADDER_RUN_TWO=4, ASYNC_LADDER_FINISH_GROUP=5;
        private GoGame encodeGame;
        private int[] encodeBoard;
        private int[] encodePreviousBoard1;
        private int[] encodePreviousBoard2;
        private int[] encodeRecentMoveLoc;
        private int[] encodeRecentMovePla;
        private bool[] encodeSuperkoBanned;
        private float[] encodeSpatial;
        private float[] encodeGlobal;
        private int encodeSideToMove;
        private int encodeKoLoc;
        private int encodeKomiTimes2;
        private bool encodePositionalSuperko;
        private bool encodeMultiStoneSuicideLegal;
        private bool encodeAreaScoring;
        private int encodeConsecutivePasses;
        private int encodeGameState;
        private int encodeHistoryCount;
        private int asyncLadderStage;
        private int asyncLadderStart;
        private int asyncLadderPlaneValue;
        private int asyncLadderWorkingPlane;
        private bool asyncLadderMarkWorking;
        private int[] asyncLadderSource;
        private int asyncLadderGroupSize;
        private int asyncLadderGroupColor;
        private int asyncLadderWorkingCount;
        private int asyncLadderAttempt;
        private int asyncLadderTargetLoc;
        private int asyncLadderLiberty0;
        private int asyncLadderLiberty1;
        private bool asyncLadderResult;
        private bool asyncLadderSearchActive;
        private bool asyncLadderReturnedFromDeeper;
        private bool asyncLadderReturnValue;
        private bool asyncLadderSearchResult;
        private int asyncLadderDepth;
        private bool asyncLadderDefenderFirst;
        private float encodeStartedAt;
        private float encodeStageStartedAt;
        private float encodeStepDeadline;
        private float[] trackedSpatialBuffer;
        private int[] spatialTouchedIndices=new int[SPATIAL_COUNT];
        private int spatialTouchedCount;
        private int spatialClearCount;
        private bool spatialBufferInitialized;
        private bool encodeNeedsFullSpatialClear;
        private bool encodeNeedsTrackedSpatialClear;
        private bool trackSpatialWrites;

        public bool Encode(GoGame game, float[] spatial, float[] global)
        {
            if(game==null){lastError="game is null";return false;}
            // Keep the compatibility API on the same resumable state machine
            // as production. A caller that truly needs a synchronous result
            // still receives the exact output, but there is no second hidden
            // 361-point superko loop with different semantics.
            if(!BeginEncodeState(game.board,game.previousBoard1,game.previousBoard2,
                game.recentMoveLoc,game.recentMovePla,game.sideToMove,game.koLoc,
                game.areaScoring?game.GetEffectiveChineseWhiteCompensationTimes2():game.komiTimes2,
                game.positionalSuperko,game.multiStoneSuicideLegal,
                game.areaScoring,game.consecutivePasses,game.gameState,bannedScratch,
                spatial,global))return false;
            encodeGame=game;encodeStage=ASYNC_STAGE_SCAN_SUPERKO;encodeCursor=0;
            encodeStageStartedAt=Time.realtimeSinceStartup;
            int guard=0;
            while(encodeState==ENCODE_RUNNING&&guard<512)
            {
                StepEncode(256);guard++;
            }
            if(encodeState!=ENCODE_COMPLETE)
            {
                if(lastError=="")lastError="synchronous feature wrapper did not complete";
                return false;
            }
            return true;
        }

        public bool EncodeIntoOwnedBuffers(GoGame game)
        {
            bool ok=Encode(game,spatialOutput,globalOutput); return ok;
        }

        // The synchronous methods above remain useful for editor/reference
        // comparisons. Runtime AI uses this cooperative path: expensive
        // KataGo ladder work is resumed from Udon Update instead of running a
        // monolithic VM event that can exceed ClientSim/VRChat's time limit.
        public bool BeginEncodeGame(GoGame game)
        {
            if(game==null){lastError="game is null";encodeState=ENCODE_ERROR;return false;}
            bool started=BeginEncodeState(game.board,game.previousBoard1,game.previousBoard2,
                game.recentMoveLoc,game.recentMovePla,game.sideToMove,game.koLoc,
                game.areaScoring?game.GetEffectiveChineseWhiteCompensationTimes2():game.komiTimes2,
                game.positionalSuperko,game.multiStoneSuicideLegal,
                game.areaScoring,game.consecutivePasses,game.gameState,bannedScratch,
                spatialOutput,globalOutput);
            if(started)
            {
                encodeGame=game;encodeStage=ASYNC_STAGE_SCAN_SUPERKO;encodeCursor=0;
                encodeStageStartedAt=Time.realtimeSinceStartup;
            }
            return started;
        }

        public bool BeginEncodeIntoOwnedBuffers(GoGame game)
        {
            return BeginEncodeGame(game);
        }

        public bool BeginEncodeState(int[] board,int[] previousBoard1,int[] previousBoard2,
            int[] recentMoveLoc,int[] recentMovePla,int sideToMove,int koLoc,int komiTimes2,
            bool positionalSuperko,bool multiStoneSuicideLegal,bool areaScoring,
            int consecutivePasses,int gameState,bool[] superkoBanned,float[] spatial,
            float[] global)
        {
            if(encodeState==ENCODE_RUNNING){lastError="encoding is already running";return false;}
            if(board==null||board.Length<GoGame.AREA){lastError="board must contain 361 points";encodeState=ENCODE_ERROR;return false;}
            if(spatial==null||spatial.Length<SPATIAL_COUNT){lastError="spatial output must contain 22*361 floats";encodeState=ENCODE_ERROR;return false;}
            if(global==null||global.Length<GLOBAL_CHANNELS){lastError="global output must contain 19 floats";encodeState=ENCODE_ERROR;return false;}
            if(sideToMove!=GoGame.BLACK&&sideToMove!=GoGame.WHITE){lastError="sideToMove must be BLACK or WHITE";encodeState=ENCODE_ERROR;return false;}
            EnsureNeighborTables();
            if(!EnsureLadderStorage())
            {
                lastError="ladder storage could not be allocated";encodeState=ENCODE_ERROR;return false;
            }
            encodeGame=null;
            lastSpatialClearCount=0;lastSpatialClearSlices=0;
            lastEncodeUsedFreshOwnedSpatial=false;
            PrepareSpatialBuffer(spatial,global);
            encodeBoard=board;encodePreviousBoard1=previousBoard1;encodePreviousBoard2=previousBoard2;
            encodeRecentMoveLoc=recentMoveLoc;encodeRecentMovePla=recentMovePla;
            encodeSuperkoBanned=superkoBanned==null?bannedScratch:superkoBanned;
            encodeSpatial=spatial;encodeGlobal=global;encodeSideToMove=sideToMove;
            encodeKoLoc=koLoc;encodeKomiTimes2=komiTimes2;
            encodePositionalSuperko=positionalSuperko;
            encodeMultiStoneSuicideLegal=multiStoneSuicideLegal;
            encodeAreaScoring=areaScoring;encodeConsecutivePasses=consecutivePasses;
            encodeGameState=gameState;encodeHistoryCount=0;
            encodeState=ENCODE_RUNNING;
            encodeStage=(encodeNeedsFullSpatialClear||encodeNeedsTrackedSpatialClear)
                ?ASYNC_STAGE_CLEAR_SPATIAL:ASYNC_STAGE_BASE;
            encodeCursor=0;encodeLadderPlane=0;encodeLadderTransitions=0;
            encodeGeneration++;
            if(encodeGeneration==0)encodeGeneration=1;
            lastEncodeMilliseconds=0f;lastScanSuperkoMilliseconds=0f;
            lastClearSpatialMilliseconds=0f;lastBaseMilliseconds=0f;
            lastLadderMilliseconds=0f;lastAreaMilliseconds=0f;
            lastEncodeSteps=0;lastLadderSearchTransitions=0;lastLadderNodes=0;
            lastLadderCacheHits=0;lastLadderCacheMisses=0;
            lastEncodeStepMilliseconds=0f;maxEncodeStepMilliseconds=0f;
            featureActiveCpuMilliseconds=0f;ladderActiveCpuMilliseconds=0f;
            ladderSliceCalls=0;ladderDfsCalls=0;maxLadderTransitionsOneSlice=0;
            maxLadderSliceMilliseconds=0f;ladderGroupProbeCalls=0;
            ladderGroupPointsVisited=0;ladderCacheLookups=0;
            ladderCacheEntriesChecked=0;ladderCacheKeyComparisons=0;
            ladderCacheFullHits=0;ladderCacheMarkerHits=0;
            ladderCacheWorkingOnlyHits=0;ladderWorkConsumedThisCall=0;
            encodeStartedAt=Time.realtimeSinceStartup;
            encodeStageStartedAt=encodeStartedAt;
            lastError="";activeBoard=null;
            return true;
        }

        /// <summary>
        /// Prepares fixed encoder/ladder storage without starting an encode.
        /// This is a resource warm-up only; no feature values or rule state
        /// are changed.
        /// </summary>
        public bool PrepareRuntimeStorage()
        {
            EnsureNeighborTables();
            if(EnsureLadderStorage())return true;
            lastError="ladder storage could not be allocated";
            return false;
        }

        public void CancelEncode()
        {
            encodeGame=null;encodeBoard=null;encodePreviousBoard1=null;encodePreviousBoard2=null;
            encodeRecentMoveLoc=null;encodeRecentMovePla=null;encodeSuperkoBanned=null;
            encodeSpatial=null;encodeGlobal=null;activeBoard=null;
            encodeState=ENCODE_CANCELLED;encodeStage=ASYNC_STAGE_CLEAR_SPATIAL;encodeCursor=0;
            asyncLadderSearchActive=false;
        }

        private void PrepareSpatialBuffer(float[] spatial,float[] global)
        {
            bool sameBuffer=spatialBufferInitialized&&trackedSpatialBuffer==spatial;
            bool freshOwnedBuffer=!spatialBufferInitialized&&trackedSpatialBuffer==null&&
                spatial==spatialOutput;
            trackedSpatialBuffer=spatial;
            if(freshOwnedBuffer)
            {
                // A newly allocated float[] is already all zero. Skip the
                // synthetic full clear for the production-owned output; later
                // requests use the sparse write list, while external buffers
                // retain the conservative full-clear behavior.
                spatialClearCount=0;spatialTouchedCount=0;trackSpatialWrites=true;
                encodeNeedsTrackedSpatialClear=false;encodeNeedsFullSpatialClear=false;
                lastEncodeUsedFreshOwnedSpatial=true;
            }
            else if(sameBuffer)
            {
                // Do not clear the previous write list in BeginEncodeState.
                // It can contain almost the full 22*361 tensor, so defer that
                // work to the cooperative CLEAR_SPATIAL stage below.
                spatialClearCount=spatialTouchedCount;
                encodeNeedsTrackedSpatialClear=spatialClearCount>0;
                trackSpatialWrites=!encodeNeedsTrackedSpatialClear;
                encodeNeedsFullSpatialClear=false;
                lastEncodeUsedFreshOwnedSpatial=false;
            }
            else
            {
                spatialClearCount=0;spatialTouchedCount=0;trackSpatialWrites=false;
                encodeNeedsTrackedSpatialClear=false;encodeNeedsFullSpatialClear=true;
                lastEncodeUsedFreshOwnedSpatial=false;
            }
            lastSpatialClearCount=encodeNeedsFullSpatialClear?SPATIAL_COUNT:spatialClearCount;
            lastSpatialClearSlices=0;
            for(int i=0;i<GLOBAL_CHANNELS;i++)global[i]=0f;
        }

        private void SetSpatialOne(float[] spatial,int index)
        {
            if(index<0||index>=SPATIAL_COUNT||spatial[index]==1f)return;
            spatial[index]=1f;
            if(trackSpatialWrites&&spatialTouchedCount<spatialTouchedIndices.Length)
                spatialTouchedIndices[spatialTouchedCount++]=index;
        }

        public int StepEncode(int workBudget)
        {
            return StepEncodeUntil(workBudget,-1f);
        }

        /// <summary>
        /// Deadline-aware production entry point. The legacy wrapper keeps
        /// synchronous editor probes intact, while the controller supplies its
        /// single admitted-frame deadline so the encoder cannot mint a fresh
        /// minimum slice after the caller's budget is exhausted.
        /// </summary>
        public int StepEncodeUntil(int workBudget,float deadline)
        {
            if(encodeState!=ENCODE_RUNNING)return encodeState;
            float stepStartedAt=Time.realtimeSinceStartup;
            if(deadline>0f)
            {
                encodeStepDeadline=deadline;
            }
            else
            {
                float stepBudgetMilliseconds=Mathf.Clamp(workBudget/128f,0.25f,4.0f);
                encodeStepDeadline=stepStartedAt+stepBudgetMilliseconds*0.001f;
            }
            diagnosticLadderStart=asyncLadderStart;
            diagnosticLadderStage=asyncLadderStage;
            diagnosticLadderDepth=asyncLadderDepth;
            diagnosticLadderNodes=ladderNodeCount;
            diagnosticLadderTransitions=encodeLadderTransitions;
            diagnosticLadderPlane=encodeLadderPlane;
            diagnosticLadderSearchActive=asyncLadderSearchActive;
            int budget=Mathf.Clamp(workBudget,1,256);
            stepLadderTransitions=0;stepLadderSlices=0;stepLadderCpuMilliseconds=0f;
            lastEncodeSteps++;
            int stageGuard=0;
            while(encodeState==ENCODE_RUNNING&&stageGuard++<8)
            {
                if(encodeStage==ASYNC_STAGE_SCAN_SUPERKO)
                {
                    int completed=0;
                    while(encodeCursor<GoGame.AREA&&completed<budget&&
                        (!EncodeStepDeadlineReached()||completed==0))
                    {
                        encodeSuperkoBanned[encodeCursor]=encodeGame.IsSuperkoBanned(encodeCursor);
                        encodeCursor++;completed++;
                    }
                    if(encodeCursor<GoGame.AREA)return FinishEncodeStep(stepStartedAt);
                    FinishTimedStage(ASYNC_STAGE_SCAN_SUPERKO);
                    encodeStage=(encodeNeedsFullSpatialClear||encodeNeedsTrackedSpatialClear)
                        ?ASYNC_STAGE_CLEAR_SPATIAL:ASYNC_STAGE_BASE;
                    encodeCursor=0;encodeStageStartedAt=Time.realtimeSinceStartup;
                    continue;
                }

                if(encodeStage==ASYNC_STAGE_CLEAR_SPATIAL)
                {
                    lastSpatialClearSlices++;
                    bool fullClear=encodeNeedsFullSpatialClear;
                    int clearLength=fullClear?SPATIAL_COUNT:spatialClearCount;
                    int clearQuota=Mathf.Clamp(budget*32,32,512);
                    int end=encodeCursor+clearQuota;if(end>clearLength)end=clearLength;
                    int cleared=0;
                    while(encodeCursor<end&&(!EncodeStepDeadlineReached()||cleared==0))
                    {
                        int spatialIndex=fullClear?encodeCursor:spatialTouchedIndices[encodeCursor];
                        encodeSpatial[spatialIndex]=0f;
                        encodeCursor++;cleared++;
                    }
                    if(encodeCursor<clearLength)return FinishEncodeStep(stepStartedAt);
                    spatialBufferInitialized=true;trackSpatialWrites=true;spatialTouchedCount=0;
                    spatialClearCount=0;encodeNeedsTrackedSpatialClear=false;
                    encodeNeedsFullSpatialClear=false;
                    for(int i=0;i<GLOBAL_CHANNELS;i++)encodeGlobal[i]=0f;
                    FinishTimedStage(ASYNC_STAGE_CLEAR_SPATIAL);
                    encodeStage=ASYNC_STAGE_BASE;encodeCursor=0;
                    encodeStageStartedAt=Time.realtimeSinceStartup;
                    continue;
                }

                if(encodeStage==ASYNC_STAGE_BASE)
                {
                    activeBoard=encodeBoard;
                    MarkStoneAndLibertyPlanes(encodeSpatial,encodeSideToMove);
                    MarkKoPlane(encodeKoLoc,encodePositionalSuperko,encodeSuperkoBanned,encodeSpatial);
                    int historyLimit=encodeGameState==GoGame.STATE_PLAYING?5:1;
                    encodeHistoryCount=MarkHistoryPlanes(encodeRecentMoveLoc,encodeRecentMovePla,
                        encodeSideToMove,historyLimit,encodeSpatial,encodeGlobal);
                    BeginAsyncLadderPlane(encodeBoard,14,17,encodeSideToMove,true);
                    FinishTimedStage(ASYNC_STAGE_BASE);
                    encodeLadderPlane=0;encodeStage=ASYNC_STAGE_LADDER;
                    encodeStageStartedAt=Time.realtimeSinceStartup;
                    // Deliberately fall through to ladder in this same slice.
                    continue;
                }

                if(encodeStage==ASYNC_STAGE_LADDER)
                {
                    int ladderBudget=Mathf.Clamp(budget,4,256);
                    int remaining=ladderBudget;
                    while(encodeStage==ASYNC_STAGE_LADDER&&remaining>0)
                    {
                        float ladderStartedAt=Time.realtimeSinceStartup;
                        bool planeComplete=StepAsyncLadderPlane(remaining);
                        float ladderSliceMs=(Time.realtimeSinceStartup-ladderStartedAt)*1000f;
                        stepLadderCpuMilliseconds+=ladderSliceMs;stepLadderSlices++;
                        int used=ladderWorkConsumedThisCall;
                        if(used<0)used=0;if(used>remaining)used=remaining;
                        remaining-=used;
                        if(!planeComplete)return FinishEncodeStep(stepStartedAt);
                        encodeLadderPlane++;
                        if(encodeLadderPlane==1)
                        {
                            int[] previous=encodeHistoryCount>=1&&encodePreviousBoard1!=null&&encodePreviousBoard1.Length>=GoGame.AREA?encodePreviousBoard1:encodeBoard;
                            BeginAsyncLadderPlane(previous,15,-1,encodeSideToMove,false);
                            continue;
                        }
                        if(encodeLadderPlane==2)
                        {
                            int[] previousPrevious=encodeHistoryCount>=2&&encodePreviousBoard2!=null&&encodePreviousBoard2.Length>=GoGame.AREA?encodePreviousBoard2:(encodeHistoryCount>=1&&encodePreviousBoard1!=null&&encodePreviousBoard1.Length>=GoGame.AREA?encodePreviousBoard1:encodeBoard);
                            BeginAsyncLadderPlane(previousPrevious,16,-1,encodeSideToMove,false);
                            continue;
                        }
                        FinishTimedStage(ASYNC_STAGE_LADDER);
                        encodeStage=ASYNC_STAGE_AREA;encodeStageStartedAt=Time.realtimeSinceStartup;
                    }
                    if(encodeStage==ASYNC_STAGE_LADDER)return FinishEncodeStep(stepStartedAt);
                    continue;
                }

                if(encodeStage==ASYNC_STAGE_AREA)
                {
                    activeBoard=encodeBoard;
                    MarkAreaPlanes(encodeAreaScoring,encodeSideToMove,encodeSpatial);
                    encodeGlobal[5]=SelfKomi(encodeKomiTimes2,encodeSideToMove)/20f;
                    if(encodePositionalSuperko){encodeGlobal[6]=1f;encodeGlobal[7]=0.5f;}
                    if(encodeMultiStoneSuicideLegal)encodeGlobal[8]=1f;
                    if(!encodeAreaScoring)encodeGlobal[9]=1f;
                    encodeGlobal[14]=encodeConsecutivePasses>0?1f:0f;
                    encodeGlobal[18]=ParityWave(SelfKomi(encodeKomiTimes2,encodeSideToMove));
                    FinishTimedStage(ASYNC_STAGE_AREA);
                    lastEncodeMilliseconds=(Time.realtimeSinceStartup-encodeStartedAt)*1000f;
                    activeBoard=null;encodeGame=null;encodeState=ENCODE_COMPLETE;
                    encodeStage=ASYNC_STAGE_AREA;encodeCursor=0;
                    break;
                }
                break;
            }
            return FinishEncodeStep(stepStartedAt);
        }

        private int FinishEncodeStep(float startedAt)
        {
            lastEncodeStepMilliseconds=(Time.realtimeSinceStartup-startedAt)*1000f;
            if(lastEncodeStepMilliseconds>maxEncodeStepMilliseconds)
                maxEncodeStepMilliseconds=lastEncodeStepMilliseconds;
            featureActiveCpuMilliseconds+=lastEncodeStepMilliseconds;
            ladderActiveCpuMilliseconds+=stepLadderCpuMilliseconds;
            ladderSliceCalls+=stepLadderSlices;
            if(stepLadderTransitions>maxLadderTransitionsOneSlice)
                maxLadderTransitionsOneSlice=stepLadderTransitions;
            if(stepLadderCpuMilliseconds>maxLadderSliceMilliseconds)
                maxLadderSliceMilliseconds=stepLadderCpuMilliseconds;
            return encodeState;
        }

        private void BeginAsyncLadderPlane(int[] source,int plane,int workingPlane,int pla,bool markWorking)
        {
            if(ladderAttemptActive)EndLadderAttempt();
            asyncLadderSource=source;asyncLadderPlaneValue=plane;asyncLadderWorkingPlane=workingPlane;
            asyncLadderMarkWorking=markWorking;asyncLadderStart=0;asyncLadderStage=ASYNC_LADDER_SCAN;
            asyncLadderGroupSize=0;asyncLadderGroupColor=0;asyncLadderWorkingCount=0;
            asyncLadderAttempt=0;asyncLadderResult=false;asyncLadderSearchActive=false;
            activeBoard=source;
            BeginGroupProcessedPass();
            asyncLadderCacheCursor=0;asyncLadderCacheHit=false;asyncLadderWorkingOnly=false;
            PackLadderCacheQuery(source);
            asyncLadderCacheEntry=FindLadderCache(pla,markWorking);
            if(asyncLadderCacheEntry>=0)
            {
                lastLadderCacheHits++;
                ladderCacheFullHits++;
                asyncLadderCacheHit=true;
                return;
            }
            int markerEntry=FindLadderMarkerCache();
            if(!markWorking&&markerEntry>=0)
            {
                lastLadderCacheHits++;
                ladderCacheMarkerHits++;
                asyncLadderCacheEntry=markerEntry;asyncLadderCacheHit=true;
                return;
            }
            if(markWorking&&markerEntry>=0)
            {
                lastLadderCacheHits++;
                ladderCacheWorkingOnlyHits++;
                asyncLadderCacheEntry=markerEntry;
                asyncLadderCacheHit=true;asyncLadderWorkingOnly=true;
                return;
            }
            lastLadderCacheMisses++;
            asyncLadderCacheEntry=AllocateLadderCache(pla,markWorking);
            // A cache hit does not need a working board at all. Only a miss
            // (or the working-only fallback below) initializes the mutable
            // board used by the exact DFS.
            Copy(source,ladderBoard);
        }

        private bool StepAsyncLadderPlane(int workBudget)
        {
            int work=0;ladderWorkConsumedThisCall=0;
            if(asyncLadderCacheHit)
            {
                while(asyncLadderCacheCursor<GoGame.AREA&&work<workBudget&&
                    (!EncodeStepDeadlineReached()||work==0))
                {
                    int loc=asyncLadderCacheCursor++;
                    if(IsPackedMaskSet(ladderCacheMaskPacked,asyncLadderCacheEntry,loc))
                        SetSpatialOne(encodeSpatial,asyncLadderPlaneValue*GoGame.AREA+loc);
                    if(!asyncLadderWorkingOnly&&asyncLadderMarkWorking&&
                        IsPackedMaskSet(ladderCacheWorkingPacked,asyncLadderCacheEntry,loc))
                        SetSpatialOne(encodeSpatial,asyncLadderWorkingPlane*GoGame.AREA+loc);
                    work++;
                }
                if(asyncLadderCacheCursor<GoGame.AREA)return FinishLadderPlaneStep(false,work);
                if(!asyncLadderWorkingOnly)return FinishLadderPlaneStep(true,work);
                asyncLadderCacheHit=false;asyncLadderCacheCursor=0;
                asyncLadderStart=0;asyncLadderStage=ASYNC_LADDER_SCAN;
                Copy(asyncLadderSource,ladderBoard);
                BeginGroupProcessedPass();
                return FinishLadderPlaneStep(false,work);
            }
            while(work<workBudget&&(!EncodeStepDeadlineReached()||work==0))
            {
                if(asyncLadderStage==ASYNC_LADDER_SCAN)
                {
                    if(asyncLadderStart>=GoGame.AREA)
                    {CommitLadderCache();activeBoard=asyncLadderSource;return FinishLadderPlaneStep(true,work);}
                    int start=asyncLadderStart++;
                    int color=asyncLadderSource[start];
                    if(color==GoGame.EMPTY||groupProcessedStamp[start]==groupProcessedGeneration){work++;continue;}
                    activeBoard=asyncLadderSource;
                    int size=CollectGroup(start,color);int libs=CountLiberties();
                    for(int i=0;i<size;i++)
                    {
                        int loc=groupMembers[i];groupProcessedStamp[loc]=groupProcessedGeneration;
                    }
                    if(asyncLadderWorkingOnly)
                    {
                        bool cachedLadder=false;
                        for(int i=0;i<size;i++)
                            if(IsPackedMaskSet(ladderCacheMaskPacked,asyncLadderCacheEntry,groupMembers[i])){cachedLadder=true;break;}
                        if(color!= -encodeSideToMove||!cachedLadder){work++;continue;}
                        // A one-liberty group can contribute to the ladder
                        // mask, but the defender-first single-liberty path
                        // never emits a working move. The mask was already
                        // copied from the exact cache, so rescanning its DFS
                        // here would be pure duplicate work.
                        if(libs==1){work++;continue;}
                    }
                    if(libs!=1&&libs!=2){work++;continue;}
                    for(int i=0;i<size;i++)markedGroup[i]=groupMembers[i];
                    asyncLadderGroupSize=size;asyncLadderGroupColor=color;asyncLadderWorkingCount=0;
                    if(libs==1)
                    {
                        asyncLadderTargetLoc=start;asyncLadderDefenderFirst=true;
                        BeginAsyncLadderSearch(asyncLadderSource,start,true);
                        asyncLadderStage=ASYNC_LADDER_RUN_SINGLE;work++;
                        continue;
                    }
                    if(libs==2)
                    {
                        asyncLadderTargetLoc=start;asyncLadderDefenderFirst=true;
                        int libertyCount=CollectLiberties(libertyList);
                        asyncLadderLiberty0=libertyCount>0?libertyList[0]:GoGame.NONE;
                        asyncLadderLiberty1=libertyCount>1?libertyList[1]:GoGame.NONE;
                        asyncLadderAttempt=0;asyncLadderStage=ASYNC_LADDER_INIT_TWO;work++;
                        continue;
                    }
                    work++;
                    continue;
                }

                if(asyncLadderStage==ASYNC_LADDER_RUN_SINGLE)
                {
                    int remaining=workBudget-work;
                    int used=StepAsyncLadderSearch(remaining);
                    work+=used;
                    if(asyncLadderSearchActive)
                    {
                        if(used==0)return FinishLadderPlaneStep(false,work);
                        continue;
                    }
                    asyncLadderResult=asyncLadderSearchResult;asyncLadderStage=ASYNC_LADDER_FINISH_GROUP;work++;
                    continue;
                }

                if(asyncLadderStage==ASYNC_LADDER_INIT_TWO)
                {
                    if(asyncLadderAttempt>=2)
                    {
                        asyncLadderResult=asyncLadderWorkingCount>0;
                        asyncLadderStage=ASYNC_LADDER_FINISH_GROUP;work++;
                        continue;
                    }
                    int liberty=asyncLadderAttempt==0?asyncLadderLiberty0:asyncLadderLiberty1;
                    BeginLadderAttempt();
                    ladderTargetColor=asyncLadderGroupColor;
                    ladderKoLoc=GoGame.NONE;ladderNodeCount=0;activeBoard=ladderBoard;
                    if(!PlayLadderMove(liberty,-asyncLadderGroupColor))
                    {
                        EndLadderAttempt();
                        asyncLadderAttempt++;work++;continue;
                    }
                    ladderKoLoc=GoGame.NONE;BeginAsyncLadderSearch(ladderBoard,asyncLadderTargetLoc,true);
                    asyncLadderStage=ASYNC_LADDER_RUN_TWO;work++;
                    continue;
                }

                if(asyncLadderStage==ASYNC_LADDER_RUN_TWO)
                {
                    int remaining=workBudget-work;
                    int used=StepAsyncLadderSearch(remaining);
                    work+=used;
                    if(asyncLadderSearchActive)
                    {
                        if(used==0)return FinishLadderPlaneStep(false,work);
                        continue;
                    }
                    if(asyncLadderSearchResult)
                    {
                        int liberty=asyncLadderAttempt==0?asyncLadderLiberty0:asyncLadderLiberty1;
                        ladderWorkingMoves[asyncLadderWorkingCount++]=liberty;
                    }
                    asyncLadderAttempt++;asyncLadderStage=ASYNC_LADDER_INIT_TWO;work++;
                    continue;
                }

                if(asyncLadderStage==ASYNC_LADDER_FINISH_GROUP)
                {
                    if(asyncLadderResult)
                    {
                        for(int i=0;i<asyncLadderGroupSize;i++)
                            SetSpatialOne(encodeSpatial,asyncLadderPlaneValue*GoGame.AREA+markedGroup[i]);
                        if(asyncLadderMarkWorking&&asyncLadderWorkingPlane>=0&&asyncLadderGroupColor==-encodeSideToMove)
                            for(int i=0;i<asyncLadderWorkingCount;i++)
                                SetSpatialOne(encodeSpatial,asyncLadderWorkingPlane*GoGame.AREA+ladderWorkingMoves[i]);
                    }
                    asyncLadderStage=ASYNC_LADDER_SCAN;work++;
                    continue;
                }
            }
            return FinishLadderPlaneStep(false,work);
        }

        private bool FinishLadderPlaneStep(bool complete,int work)
        {
            ladderWorkConsumedThisCall=work;
            return complete;
        }

        private bool EncodeStepDeadlineReached()
        {
            return encodeStepDeadline>0f&&Time.realtimeSinceStartup>=encodeStepDeadline;
        }

        private bool EnsureLadderStorage()
        {
            if(ladderSnapshots!=null&&ladderMoveBuffer!=null&&
                ladderSnapshots.Length>=MAX_LADDER_DEPTH*GoGame.AREA*2&&
                ladderMoveBuffer.Length>=MAX_LADDER_DEPTH*GoGame.AREA)
            {
                ladderStorageAllocated=true;return true;
            }
            ladderSnapshots=new int[MAX_LADDER_DEPTH*GoGame.AREA*2];
            ladderMoveBuffer=new int[MAX_LADDER_DEPTH*GoGame.AREA];
            ladderStorageAllocated=ladderSnapshots!=null&&ladderMoveBuffer!=null;
            return ladderStorageAllocated;
        }

        public void ReleaseHeavyResources()
        {
            if(encodeState==ENCODE_RUNNING)CancelEncode();
            if(ladderAttemptActive)EndLadderAttempt();
            ladderSnapshots=null;ladderMoveBuffer=null;
            ladderStorageAllocated=false;ladderSnapshotActiveDepth=-1;
            asyncLadderSearchActive=false;
        }

        public int GetEstimatedAllocatedArrayBytes()
        {
            int integers=groupQueue.Length+groupMembers.Length+savedTargetGroup.Length+
                captureGroupMembers.Length+captureLibertyList.Length+
                markedGroup.Length+visitedStamp.Length+libertyStamp.Length+
                leftNeighbor.Length+rightNeighbor.Length+upNeighbor.Length+downNeighbor.Length+
                groupProcessedStamp.Length+baseGroupProcessedStamp.Length+
                areaOwner.Length+libertyList.Length+generatedTargetLiberties.Length+ladderCaptureProbeStamp.Length+
                ladderCaptureProbeMove.Length+ladderBoard.Length+
                ladderCacheSide.Length+ladderCacheKey.Length+
                ladderCacheMaskPacked.Length+ladderCacheWorkingPacked.Length+
                ladderCacheQueryKey.Length+ladderCacheSetNextWay.Length+
                ladderMoveCaptureStamp.Length+
                ladderChangedStamp.Length+ladderChangedLocations.Length+
                ladderChangedOriginal.Length+
                ladderSnapshotChangedCount.Length+ladderSnapshotStamp.Length+
                ladderSnapshotMark.Length+ladderMoveCur.Length+ladderMoveLen.Length+
                ladderKoAtDepth.Length+ladderWorkingMoves.Length+
                ladderInitialLiberties.Length+ladderTrialChangedLoc.Length+
                ladderTrialOldValue.Length+spatialTouchedIndices.Length;
            if(ladderSnapshots!=null)integers+=ladderSnapshots.Length;
            if(ladderMoveBuffer!=null)integers+=ladderMoveBuffer.Length;
            int booleans=areaVisited.Length+bannedScratch.Length+
                ladderTrialMarked.Length+ladderCacheValid.Length+
                ladderCacheHasWorking.Length;
            int floats=spatialOutput.Length+globalOutput.Length;
            return integers*4+booleans+floats*4;
        }

        private void PackLadderCacheQuery(int[] source)
        {
            for(int i=0;i<LADDER_CACHE_KEY_WORDS;i++)ladderCacheQueryKey[i]=0;
            for(int i=0;i<GoGame.AREA;i++)
            {
                int value=source[i];
                int code=value==GoGame.BLACK?1:value==GoGame.WHITE?2:0;
                int word=i>>4;int shift=(i&15)*2;
                ladderCacheQueryKey[word]|=code<<shift;
            }
            int hash=17;
            for(int i=0;i<LADDER_CACHE_KEY_WORDS;i++)hash=hash*31+ladderCacheQueryKey[i];
            ladderCacheQuerySet=hash&(LADDER_CACHE_SET_COUNT-1);
        }

        private bool LadderCacheKeyMatches(int entry)
        {
            int offset=entry*LADDER_CACHE_KEY_WORDS;
            for(int i=0;i<LADDER_CACHE_KEY_WORDS;i++)
            {
                ladderCacheKeyComparisons++;
                if(ladderCacheKey[offset+i]!=ladderCacheQueryKey[i])return false;
            }
            return true;
        }

        private int FindLadderCache(int pla,bool markWorking)
        {
            ladderCacheLookups++;
            int first=ladderCacheQuerySet*LADDER_CACHE_WAYS;
            int last=first+LADDER_CACHE_WAYS;
            for(int entry=first;entry<last;entry++)
            {
                ladderCacheEntriesChecked++;
                if(!ladderCacheValid[entry]||ladderCacheSide[entry]!=pla||
                    (markWorking&&!ladderCacheHasWorking[entry]))continue;
                if(LadderCacheKeyMatches(entry))return entry;
            }
            return -1;
        }

        private int FindLadderMarkerCache()
        {
            ladderCacheLookups++;
            int first=ladderCacheQuerySet*LADDER_CACHE_WAYS;
            int last=first+LADDER_CACHE_WAYS;
            for(int entry=first;entry<last;entry++)
            {
                ladderCacheEntriesChecked++;
                if(!ladderCacheValid[entry])continue;
                if(LadderCacheKeyMatches(entry))return entry;
            }
            return -1;
        }

        private int AllocateLadderCache(int pla,bool markWorking)
        {
            int set=ladderCacheQuerySet;
            int way=ladderCacheSetNextWay[set];
            ladderCacheSetNextWay[set]=(way+1)%LADDER_CACHE_WAYS;
            int entry=set*LADDER_CACHE_WAYS+way;
            int keyOffset=entry*LADDER_CACHE_KEY_WORDS;
            for(int i=0;i<LADDER_CACHE_KEY_WORDS;i++)ladderCacheKey[keyOffset+i]=ladderCacheQueryKey[i];
            int maskOffset=entry*LADDER_CACHE_MASK_WORDS;
            ladderCacheValid[entry]=false;ladderCacheHasWorking[entry]=markWorking;
            ladderCacheSide[entry]=pla;
            for(int i=0;i<LADDER_CACHE_MASK_WORDS;i++)
            {
                ladderCacheMaskPacked[maskOffset+i]=0;
                ladderCacheWorkingPacked[maskOffset+i]=0;
            }
            return entry;
        }

        private bool IsPackedMaskSet(int[] packed,int entry,int loc)
        {
            int offset=entry*LADDER_CACHE_MASK_WORDS+(loc>>5);
            return (packed[offset]&(1<<(loc&31)))!=0;
        }

        private void SetPackedMask(int[] packed,int entry,int loc)
        {
            int offset=entry*LADDER_CACHE_MASK_WORDS+(loc>>5);
            packed[offset]|=1<<(loc&31);
        }

        private void CommitLadderCache()
        {
            if(asyncLadderCacheEntry<0)return;
            if(asyncLadderWorkingOnly)
            {
                ladderCacheSide[asyncLadderCacheEntry]=encodeSideToMove;
                ladderCacheHasWorking[asyncLadderCacheEntry]=true;
            }
            int planeOffset=asyncLadderPlaneValue*GoGame.AREA;
            int workingOffset=asyncLadderWorkingPlane<0?-1:
                asyncLadderWorkingPlane*GoGame.AREA;
            for(int i=0;i<GoGame.AREA;i++)
            {
                if(encodeSpatial[planeOffset+i]>0.5f)
                    SetPackedMask(ladderCacheMaskPacked,asyncLadderCacheEntry,i);
                if(workingOffset>=0&&encodeSpatial[workingOffset+i]>0.5f)
                    SetPackedMask(ladderCacheWorkingPacked,asyncLadderCacheEntry,i);
            }
            // A partially computed entry is never visible to a later lookup.
            // Publish validity only after both packed outputs are complete.
            ladderCacheValid[asyncLadderCacheEntry]=true;
        }

        private void BeginAsyncLadderSearch(int[] board,int targetLoc,bool defenderFirst)
        {
            if(!ladderAttemptActive)BeginLadderAttempt();
            asyncLadderTargetLoc=targetLoc;
            ladderTargetColor=ladderBoard[targetLoc];
            ladderKoLoc=GoGame.NONE;
            ladderSnapshotActiveDepth=-1;
            asyncLadderDefenderFirst=defenderFirst;asyncLadderDepth=0;
            asyncLadderReturnedFromDeeper=false;asyncLadderReturnValue=false;
            asyncLadderSearchResult=false;asyncLadderSearchActive=true;ladderNodeCount=0;
            // Deeper frames are initialized exactly when the DFS descends.
            // Clearing all ~543 depth slots for every candidate group was a
            // hidden atomic cost in the otherwise cooperative encoder.
            ladderMoveCur[0]=-1;ladderMoveLen[0]=0;
            ladderKoAtDepth[0]=GoGame.NONE;
        }

        private int StepAsyncLadderSearch(int transitionBudget)
        {
            int completed=0;
            ladderDfsCalls++;
            while(asyncLadderSearchActive&&completed<transitionBudget)
            {
                // The enclosing plane loop already enforces the same slice
                // deadline. Sampling it every few DFS transitions avoids a
                // costly realtime call for every tiny ladder state while
                // keeping the overshoot bounded and cooperative.
                if(completed>0&&(completed&3)==0&&EncodeStepDeadlineReached())break;
                completed++;lastLadderSearchTransitions++;encodeLadderTransitions++;
                stepLadderTransitions++;int depth=asyncLadderDepth;
                if(depth<0)
                {
                    activeBoard=ladderBoard;asyncLadderSearchResult=asyncLadderReturnValue;
                    asyncLadderSearchActive=false;EndLadderAttempt();break;
                }
                if(depth>=MAX_LADDER_DEPTH-1)
                {
                    asyncLadderReturnValue=true;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;
                }
                activeBoard=ladderBoard;
                bool isDefender=(asyncLadderDefenderFirst&&(depth&1)==0)||(!asyncLadderDefenderFirst&&(depth&1)==1);
                if(ladderBoard[asyncLadderTargetLoc]!=ladderTargetColor)
                {
                    asyncLadderReturnValue=true;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;
                }
                if(ladderMoveCur[depth]<0)
                {
                    int libs=GetActiveGroupLiberties(asyncLadderTargetLoc,ladderTargetColor);
                    if(!isDefender&&libs<=1){asyncLadderReturnValue=true;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                    if(!isDefender&&libs>=3){asyncLadderReturnValue=false;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                    if(isDefender&&libs>=2){asyncLadderReturnValue=false;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                    if(isDefender&&ladderKoLoc!=GoGame.NONE){asyncLadderReturnValue=false;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                    int len=GenerateLadderMoves(depth,asyncLadderTargetLoc,ladderTargetColor,isDefender,savedTargetGroup,ladderTargetGroupSize);
                    ladderMoveLen[depth]=len;ladderMoveCur[depth]=0;
                    if(len==0){asyncLadderReturnValue=isDefender;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                }
                else
                {
                    if(asyncLadderReturnedFromDeeper){RestoreLadderPath(depth);asyncLadderReturnedFromDeeper=false;}
                    if(isDefender&&!asyncLadderReturnValue){asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                    if(!isDefender&&asyncLadderReturnValue){asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;}
                    ladderMoveCur[depth]++;
                }
                if(ladderMoveCur[depth]>=ladderMoveLen[depth])
                {
                    asyncLadderReturnValue=isDefender;asyncLadderReturnedFromDeeper=true;asyncLadderDepth--;continue;
                }
                int move=ladderMoveBuffer[depth*GoGame.AREA+ladderMoveCur[depth]];
                int player=isDefender?ladderTargetColor:-ladderTargetColor;
                SaveLadderSnapshot(depth);activeBoard=ladderBoard;
                if(!PlayLadderMove(move,player))
                {
                    // A suicide/ko rejection restores its own mutation
                    // journal; an empty/ko guard can return before opening a
                    // mutation, so close that empty snapshot here.
                    if(ladderSnapshotActiveDepth>=depth)RestoreLadderSnapshot(depth);
                    asyncLadderReturnValue=isDefender;
                    asyncLadderReturnedFromDeeper=false;continue;
                }
                ladderNodeCount++;lastLadderNodes++;
                if(ladderNodeCount>=LADDER_NODE_BUDGET)
                {
                    asyncLadderSearchResult=false;asyncLadderSearchActive=false;EndLadderAttempt();break;
                }
                asyncLadderDepth++;ladderMoveCur[asyncLadderDepth]=-1;ladderMoveLen[asyncLadderDepth]=0;ladderKoAtDepth[asyncLadderDepth]=GoGame.NONE;
            }
            return completed;
        }

        public bool EncodeState(int[] board,int[] previousBoard1,int[] previousBoard2,int[] recentMoveLoc,int[] recentMovePla,int sideToMove,int koLoc,int komiTimes2,bool positionalSuperko,bool multiStoneSuicideLegal,bool areaScoring,int consecutivePasses,int gameState,bool[] superkoBanned,float[] spatial,float[] global)
        {
            lastError="";
            if(board==null||board.Length<GoGame.AREA){lastError="board must contain 361 points";return false;}
            if(spatial==null||spatial.Length<SPATIAL_COUNT){lastError="spatial output must contain 22*361 floats";return false;}
            if(global==null||global.Length<GLOBAL_CHANNELS){lastError="global output must contain 19 floats";return false;}
            if(sideToMove!=GoGame.BLACK&&sideToMove!=GoGame.WHITE){lastError="sideToMove must be BLACK or WHITE";return false;}
            EnsureNeighborTables();
            if(!EnsureLadderStorage()){lastError="ladder storage could not be allocated";return false;}

            trackedSpatialBuffer=spatial;spatialBufferInitialized=true;
            spatialTouchedCount=0;trackSpatialWrites=true;
            for(int i=0;i<SPATIAL_COUNT;i++)spatial[i]=0f;
            for(int i=0;i<GLOBAL_CHANNELS;i++)global[i]=0f;
            activeBoard=board;
            MarkStoneAndLibertyPlanes(spatial,sideToMove);
            MarkKoPlane(koLoc,positionalSuperko,superkoBanned,spatial);
            // KataGo includes one pass-history plane after an actually
            // finished game, but hides history only when a pass is being
            // hypothetically suppressed. Our production game transitions to
            // a terminal state immediately after double-pass, so terminal
            // states use exactly one recent move here.
            int historyLimit=gameState==GoGame.STATE_PLAYING?5:1;
            int historyCount=MarkHistoryPlanes(recentMoveLoc,recentMovePla,sideToMove,historyLimit,spatial,global);

            MarkLadderPlanes(board,14,17,sideToMove,spatial,true);
            int[] previous=historyCount>=1&&previousBoard1!=null&&previousBoard1.Length>=GoGame.AREA?previousBoard1:board;
            int[] previousPrevious=historyCount>=2&&previousBoard2!=null&&previousBoard2.Length>=GoGame.AREA?previousBoard2:previous;
            MarkLadderPlanes(previous,15,-1,sideToMove,spatial,false);
            MarkLadderPlanes(previousPrevious,16,-1,sideToMove,spatial,false);

            activeBoard=board;
            MarkAreaPlanes(areaScoring,sideToMove,spatial);
            global[5]=SelfKomi(komiTimes2,sideToMove)/20f;
            if(positionalSuperko){global[6]=1f;global[7]=0.5f;}
            if(multiStoneSuicideLegal)global[8]=1f;
            if(!areaScoring)global[9]=1f;
            global[14]=consecutivePasses>0?1f:0f;
            global[18]=ParityWave(SelfKomi(komiTimes2,sideToMove));
            activeBoard=null;
            return true;
        }

        private void MarkStoneAndLibertyPlanes(float[] spatial,int pla)
        {
            baseGroupStamp++;
            if(baseGroupStamp<=0)
            {
                for(int i=0;i<GoGame.AREA;i++)baseGroupProcessedStamp[i]=0;
                baseGroupStamp=1;
            }
            for(int start=0;start<GoGame.AREA;start++){
                int color=activeBoard[start];
                if(color==GoGame.EMPTY||baseGroupProcessedStamp[start]==baseGroupStamp)continue;
                int size=CollectGroup(start,color);
                int libertyCount=CountLiberties();
                for(int i=0;i<size;i++){
                    int loc=groupMembers[i]; baseGroupProcessedStamp[loc]=baseGroupStamp;
                    SetSpatialOne(spatial,(color==pla?1:2)*GoGame.AREA+loc);
                    if(libertyCount==1)SetSpatialOne(spatial,3*GoGame.AREA+loc);
                    else if(libertyCount==2)SetSpatialOne(spatial,4*GoGame.AREA+loc);
                    else if(libertyCount==3)SetSpatialOne(spatial,5*GoGame.AREA+loc);
                }
            }
            for(int i=0;i<GoGame.AREA;i++)spatial[i]=1f;
        }

        private void MarkKoPlane(int koLoc,bool positionalSuperko,bool[] superkoBanned,float[] spatial)
        {
            if(koLoc>=0&&koLoc<GoGame.AREA)SetSpatialOne(spatial,6*GoGame.AREA+koLoc);
            if(!positionalSuperko||superkoBanned==null)return;
            int count=superkoBanned.Length<GoGame.AREA?superkoBanned.Length:GoGame.AREA;
            for(int i=0;i<count;i++)if(superkoBanned[i]&&i!=koLoc)SetSpatialOne(spatial,6*GoGame.AREA+i);
        }

        private int MarkHistoryPlanes(int[] recentMoveLoc,int[] recentMovePla,int pla,int historyLimit,float[] spatial,float[] global)
        {
            if(historyLimit<=0||recentMoveLoc==null||recentMovePla==null)return 0;
            int max=recentMoveLoc.Length<5?recentMoveLoc.Length:5; if(recentMovePla.Length<max)max=recentMovePla.Length; if(historyLimit<max)max=historyLimit;
            int count=0;
            for(int i=0;i<max;i++){
                int expected=(i&1)==0?-pla:pla;
                if(recentMovePla[i]!=expected)break;
                count=i+1; int loc=recentMoveLoc[i];
                if(loc==GoGame.PASS)global[i]=1f;
                else if(loc>=0&&loc<GoGame.AREA)SetSpatialOne(spatial,(9+i)*GoGame.AREA+loc);
            }
            return count;
        }

        private void MarkAreaPlanes(bool areaScoring,int pla,float[] spatial)
        {
            for(int i=0;i<GoGame.AREA;i++){areaVisited[i]=false;areaOwner[i]=GoGame.EMPTY;}
            for(int i=0;i<GoGame.AREA;i++)if(activeBoard[i]!=GoGame.EMPTY)areaOwner[i]=activeBoard[i];
            for(int start=0;start<GoGame.AREA;start++){
                if(activeBoard[start]!=GoGame.EMPTY||areaVisited[start])continue;
                int head=0,tail=0,count=0; bool touchesBlack=false,touchesWhite=false; groupQueue[tail++]=start; areaVisited[start]=true;
                while(head<tail){int loc=groupQueue[head++];groupMembers[count++]=loc;TouchAreaNeighbor(Left(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchAreaNeighbor(Right(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchAreaNeighbor(Up(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchAreaNeighbor(Down(loc),ref tail,ref touchesBlack,ref touchesWhite);}
                if(areaScoring){int owner=touchesBlack&&!touchesWhite?GoGame.BLACK:touchesWhite&&!touchesBlack?GoGame.WHITE:GoGame.EMPTY;for(int i=0;i<count;i++)areaOwner[groupMembers[i]]=owner;}
            }
            if(!areaScoring)return;
            for(int i=0;i<GoGame.AREA;i++){if(areaOwner[i]==pla)SetSpatialOne(spatial,18*GoGame.AREA+i);else if(areaOwner[i]==-pla)SetSpatialOne(spatial,19*GoGame.AREA+i);}
        }

        private void TouchAreaNeighbor(int loc,ref int tail,ref bool touchesBlack,ref bool touchesWhite)
        {
            if(loc<0)return; int color=activeBoard[loc];
            if(color==GoGame.BLACK)touchesBlack=true;
            else if(color==GoGame.WHITE)touchesWhite=true;
            else if(!areaVisited[loc]){areaVisited[loc]=true;groupQueue[tail++]=loc;}
        }

        private float SelfKomi(int komiTimes2,int pla){float komi=komiTimes2*0.5f;float self=pla==GoGame.WHITE?komi:-komi;float limit=GoGame.AREA+20f;if(self>limit)self=limit;if(self<-limit)self=-limit;return self;}

        private float ParityWave(float selfKomi)
        {
            float floorValue=Mathf.Floor((selfKomi-1f)/2f)*2f+1f; float delta=selfKomi-floorValue;
            if(delta<0f)delta=0f; if(delta>2f)delta=2f;
            if(delta<0.5f)return delta; if(delta<1.5f)return 1f-delta; return delta-2f;
        }

        private int CollectGroup(int start,int color)
        {
            ladderGroupProbeCalls++;
            groupStamp++;
            if(groupStamp==0)
            {
                for(int i=0;i<GoGame.AREA;i++){visitedStamp[i]=0;libertyStamp[i]=0;}
                groupStamp=1;
            }
            currentLibertyCount=0;
            int head=0,tail=0,size=0; groupQueue[tail++]=start; visitedStamp[start]=groupStamp;
            while(head<tail){int loc=groupQueue[head++];groupMembers[size++]=loc;PushGroupNeighbor(Left(loc),color,ref tail);PushGroupNeighbor(Right(loc),color,ref tail);PushGroupNeighbor(Up(loc),color,ref tail);PushGroupNeighbor(Down(loc),color,ref tail);}
            ladderGroupPointsVisited+=size;return size;
        }

        private int CollectTargetGroup(int start,int color)
        {
            ladderGroupProbeCalls++;
            groupStamp++;
            if(groupStamp==0)
            {
                for(int i=0;i<GoGame.AREA;i++){visitedStamp[i]=0;libertyStamp[i]=0;}
                groupStamp=1;
            }
            currentLibertyCount=0;
            int head=0,tail=0,size=0; groupQueue[tail++]=start; visitedStamp[start]=groupStamp;
            while(head<tail){int loc=groupQueue[head++];savedTargetGroup[size++]=loc;PushGroupNeighbor(Left(loc),color,ref tail);PushGroupNeighbor(Right(loc),color,ref tail);PushGroupNeighbor(Up(loc),color,ref tail);PushGroupNeighbor(Down(loc),color,ref tail);}
            ladderGroupPointsVisited+=size;return size;
        }

        private int CollectCaptureGroup(int start,int color)
        {
            ladderGroupProbeCalls++;
            groupStamp++;
            if(groupStamp==0)
            {
                for(int i=0;i<GoGame.AREA;i++){visitedStamp[i]=0;libertyStamp[i]=0;}
                groupStamp=1;
            }
            currentLibertyCount=0;
            int head=0,tail=0,size=0; groupQueue[tail++]=start; visitedStamp[start]=groupStamp;
            while(head<tail){int loc=groupQueue[head++];captureGroupMembers[size++]=loc;PushGroupNeighbor(Left(loc),color,ref tail);PushGroupNeighbor(Right(loc),color,ref tail);PushGroupNeighbor(Up(loc),color,ref tail);PushGroupNeighbor(Down(loc),color,ref tail);}
            ladderGroupPointsVisited+=size;return size;
        }

        private void PushGroupNeighbor(int loc,int color,ref int tail)
        {
            if(loc<0)return;
            int c=activeBoard[loc];
            if(c==GoGame.EMPTY)
            {
                if(libertyStamp[loc]!=groupStamp)
                {
                    libertyStamp[loc]=groupStamp;
                    // Keep the historical board-order contract without
                    // rescanning all 361 points in every ladder probe.
                    int insert=currentLibertyCount++;
                    while(insert>0&&libertyList[insert-1]>loc)
                    {libertyList[insert]=libertyList[insert-1];insert--;}
                    libertyList[insert]=loc;
                }
            }
            else if(c==color&&visitedStamp[loc]!=groupStamp)
            {visitedStamp[loc]=groupStamp;groupQueue[tail++]=loc;}
        }

        private int CountLiberties(){return currentLibertyCount;}

        private int CollectLiberties(int[] output)
        {
            if(output!=libertyList)
                for(int i=0;i<currentLibertyCount;i++)output[i]=libertyList[i];
            return currentLibertyCount;
        }

        private void EnsureNeighborTables()
        {
            if(neighborTablesReady)return;
            for(int loc=0;loc<GoGame.AREA;loc++)
            {
                int x=loc%GoGame.SIZE;int y=loc/GoGame.SIZE;
                leftNeighbor[loc]=x==0?GoGame.NONE:loc-1;
                rightNeighbor[loc]=x==GoGame.SIZE-1?GoGame.NONE:loc+1;
                upNeighbor[loc]=y==0?GoGame.NONE:loc-GoGame.SIZE;
                downNeighbor[loc]=y==GoGame.SIZE-1?GoGame.NONE:loc+GoGame.SIZE;
            }
            neighborTablesReady=true;
        }

        private void BeginGroupProcessedPass()
        {
            groupProcessedGeneration++;
            if(groupProcessedGeneration<=0)
            {
                for(int i=0;i<GoGame.AREA;i++)groupProcessedStamp[i]=0;
                groupProcessedGeneration=1;
            }
        }

        private int Left(int loc){return leftNeighbor[loc];}
        private int Right(int loc){return rightNeighbor[loc];}
        private int Up(int loc){return upNeighbor[loc];}
        private int Down(int loc){return downNeighbor[loc];}

        private void FinishTimedStage(int stage)
        {
            float elapsed=(Time.realtimeSinceStartup-encodeStageStartedAt)*1000f;
            if(stage==ASYNC_STAGE_SCAN_SUPERKO)lastScanSuperkoMilliseconds=elapsed;
            else if(stage==ASYNC_STAGE_CLEAR_SPATIAL)lastClearSpatialMilliseconds=elapsed;
            else if(stage==ASYNC_STAGE_BASE)lastBaseMilliseconds=elapsed;
            else if(stage==ASYNC_STAGE_LADDER)lastLadderMilliseconds=elapsed;
            else if(stage==ASYNC_STAGE_AREA)lastAreaMilliseconds=elapsed;
        }

        private void Copy(int[] source,int[] target){for(int i=0;i<GoGame.AREA;i++)target[i]=source[i];}

        private void MarkLadderPlanes(int[] source,int plane,int workingPlane,int pla,float[] spatial,bool markWorking)
        {
            activeBoard=source;Copy(source,ladderBoard);
            BeginGroupProcessedPass();
            for(int start=0;start<GoGame.AREA;start++){
                activeBoard=source;
                int color=source[start];if(color==GoGame.EMPTY||groupProcessedStamp[start]==groupProcessedGeneration)continue;
                int size=CollectGroup(start,color);int libs=CountLiberties();
                for(int i=0;i<size;i++)groupProcessedStamp[groupMembers[i]]=groupProcessedGeneration;
                if(libs!=1&&libs!=2)continue;
                for(int i=0;i<size;i++)markedGroup[i]=groupMembers[i];
                bool laddered=false;int workingCount=0;
                if(libs==1)laddered=SearchLadderCaptured(source,start,true);
                else laddered=SearchLadderCapturedAttackerFirstTwo(source,start,ref workingCount);
                if(!laddered)continue;
                for(int i=0;i<size;i++)SetSpatialOne(spatial,plane*GoGame.AREA+markedGroup[i]);
                if(markWorking&&workingPlane>=0&&color==-pla)for(int i=0;i<workingCount;i++)SetSpatialOne(spatial,workingPlane*GoGame.AREA+ladderWorkingMoves[i]);
            }
            activeBoard=source;
        }

        private bool SearchLadderCaptured(int[] source,int loc,bool defenderFirst)
        {
            if(loc<0||loc>=GoGame.AREA)return false;int color=source[loc];if(color==GoGame.EMPTY)return false;
            ladderTargetColor=color;BeginLadderAttempt();
            ladderKoLoc=GoGame.NONE;ladderSnapshotActiveDepth=-1;ladderNodeCount=0;
            return RunLadder(loc,defenderFirst);
        }

        private bool SearchLadderCapturedAttackerFirstTwo(int[] source,int loc,ref int workingCount)
        {
            workingCount=0;int color=source[loc];if(color==GoGame.EMPTY)return false;activeBoard=source;CollectGroup(loc,color);int libs=CollectLiberties(libertyList);if(libs!=2)return false;for(int i=0;i<libs;i++)ladderInitialLiberties[i]=libertyList[i];
            for(int i=0;i<libs;i++){
                BeginLadderAttempt();
                ladderTargetColor=color;ladderKoLoc=GoGame.NONE;ladderSnapshotActiveDepth=-1;activeBoard=ladderBoard;
                if(!PlayLadderMove(ladderInitialLiberties[i],-color)){EndLadderAttempt();continue;}
                ladderKoLoc=GoGame.NONE;
                ladderNodeCount=0;
                // KataGo's defender-first ladder search clears a simple-ko
                // point at the root: the ladder feature assumes the
                // defender can spend a ko threat, while later recaptures are
                // still rejected by the normal move legality check.
                if(RunLadder(loc,true))ladderWorkingMoves[workingCount++]=ladderInitialLiberties[i];
            }
            activeBoard=source;return workingCount>0;
        }

        private bool RunLadder(int targetLoc,bool defenderFirst)
        {
            if(!ladderAttemptActive)BeginLadderAttempt();
            ladderSnapshotActiveDepth=-1;for(int i=0;i<MAX_LADDER_DEPTH;i++){ladderMoveCur[i]=-1;ladderMoveLen[i]=0;ladderKoAtDepth[i]=GoGame.NONE;}
            int depth=0;bool returnedFromDeeper=false;bool returnValue=false;
            while(true){
                if(depth<0){activeBoard=ladderBoard;EndLadderAttempt();return returnValue;}
                if(depth>=MAX_LADDER_DEPTH-1){returnValue=true;returnedFromDeeper=true;depth--;continue;}
                activeBoard=ladderBoard;
                bool isDefender=(defenderFirst&&(depth&1)==0)||(!defenderFirst&&(depth&1)==1);
                if(ladderBoard[targetLoc]!=ladderTargetColor){returnValue=true;returnedFromDeeper=true;depth--;continue;}
                if(ladderMoveCur[depth]<0){
                    int libs=GetActiveGroupLiberties(targetLoc,ladderTargetColor);
                    if(!isDefender&&libs<=1){returnValue=true;returnedFromDeeper=true;depth--;continue;}
                    if(!isDefender&&libs>=3){returnValue=false;returnedFromDeeper=true;depth--;continue;}
                    if(isDefender&&libs>=2){returnValue=false;returnedFromDeeper=true;depth--;continue;}
                    if(isDefender&&ladderKoLoc!=GoGame.NONE){returnValue=false;returnedFromDeeper=true;depth--;continue;}
                    int len=GenerateLadderMoves(depth,targetLoc,ladderTargetColor,isDefender,savedTargetGroup,ladderTargetGroupSize);
                    ladderMoveLen[depth]=len;ladderMoveCur[depth]=0;
                    if(len==0){returnValue=isDefender;returnedFromDeeper=true;depth--;continue;}
                }
                else{
                    if(returnedFromDeeper){RestoreLadderPath(depth);returnedFromDeeper=false;}
                    if(isDefender&&!returnValue){returnedFromDeeper=true;depth--;continue;}
                    if(!isDefender&&returnValue){returnedFromDeeper=true;depth--;continue;}
                    ladderMoveCur[depth]++;
                }
                if(ladderMoveCur[depth]>=ladderMoveLen[depth]){returnValue=isDefender;returnedFromDeeper=true;depth--;continue;}
                int move=ladderMoveBuffer[depth*GoGame.AREA+ladderMoveCur[depth]];int player=isDefender?ladderTargetColor:-ladderTargetColor;
                SaveLadderSnapshot(depth);activeBoard=ladderBoard;
                if(!PlayLadderMove(move,player))
                {
                    if(ladderSnapshotActiveDepth>=depth)RestoreLadderSnapshot(depth);
                    returnValue=isDefender;returnedFromDeeper=false;continue;
                }
                ladderNodeCount++;
                if(ladderNodeCount>=LADDER_NODE_BUDGET){EndLadderAttempt();return false;}
                depth++;ladderMoveCur[depth]=-1;ladderMoveLen[depth]=0;ladderKoAtDepth[depth]=GoGame.NONE;
            }
        }

        private int GetActiveGroupLiberties(int loc,int color)
        {
            if(loc<0||loc>=GoGame.AREA||activeBoard[loc]!=color)
            {ladderTargetGroupSize=0;generatedTargetLibertyCount=0;return 0;}
            int size=CollectTargetGroup(loc,color);ladderTargetGroupSize=size;
            int liberties=CollectLiberties(generatedTargetLiberties);
            generatedTargetLibertyCount=liberties;
            return liberties;
        }

        private int GenerateLadderMoves(int depth,int targetLoc,int targetColor,bool defender,int[] targetGroup,int targetSize)
        {
            int len=0;
            ladderCaptureProbeGeneration++;
            if(ladderCaptureProbeGeneration<=0)
            {
                for(int i=0;i<GoGame.AREA;i++)ladderCaptureProbeStamp[i]=0;
                ladderCaptureProbeGeneration=1;
            }
            if(defender){
                for(int i=0;i<targetSize;i++){int loc=targetGroup[i];len=AddCaptureMoveIfAtari(depth,len,Left(loc),targetColor);len=AddCaptureMoveIfAtari(depth,len,Right(loc),targetColor);len=AddCaptureMoveIfAtari(depth,len,Up(loc),targetColor);len=AddCaptureMoveIfAtari(depth,len,Down(loc),targetColor);}
            }
            for(int i=0;i<generatedTargetLibertyCount;i++)
                len=AddUniqueMove(depth,len,generatedTargetLiberties[i]);
            return len;
        }

        private int AddCaptureMoveIfAtari(int depth,int length,int loc,int targetColor)
        {
            if(loc<0||activeBoard[loc]!=-targetColor)return length;
            if(ladderCaptureProbeStamp[loc]==ladderCaptureProbeGeneration)
            {
                int cached=ladderCaptureProbeMove[loc];
                return cached==GoGame.NONE?length:AddUniqueMove(depth,length,cached);
            }
            int size=CollectCaptureGroup(loc,-targetColor);
            int liberties=CollectLiberties(captureLibertyList);
            int move=liberties==1?captureLibertyList[0]:GoGame.NONE;
            for(int i=0;i<size;i++)
            {
                int member=captureGroupMembers[i];
                ladderCaptureProbeStamp[member]=ladderCaptureProbeGeneration;
                ladderCaptureProbeMove[member]=move;
            }
            return move==GoGame.NONE?length:AddUniqueMove(depth,length,move);
        }

        private int AddUniqueMove(int depth,int length,int move)
        {if(move<0||move>=GoGame.AREA)return length;int start=depth*GoGame.AREA;for(int i=0;i<length;i++)if(ladderMoveBuffer[start+i]==move)return length;ladderMoveBuffer[start+length]=move;return length+1;}

        private void SaveLadderSnapshot(int depth)
        {
            ladderSnapshotSerial++;
            if(ladderSnapshotSerial==0)
            {
                for(int i=0;i<GoGame.AREA;i++)ladderSnapshotMark[i]=0;
                ladderSnapshotSerial=1;
            }
            ladderSnapshotStamp[depth]=ladderSnapshotSerial;
            ladderSnapshotChangedCount[depth]=0;
            ladderSnapshotActiveDepth=depth;
            ladderKoAtDepth[depth]=ladderKoLoc;
        }

        private void RestoreLadderSnapshot(int depth)
        {
            int count=ladderSnapshotChangedCount[depth];
            int valueOffset=MAX_LADDER_DEPTH*GoGame.AREA;
            int stamp=ladderSnapshotStamp[depth];
            for(int i=count-1;i>=0;i--)
            {
                int offset=depth*GoGame.AREA+i;
                int loc=ladderSnapshots[offset];
                RestoreLadderPoint(loc,ladderSnapshots[valueOffset+offset]);
                if(ladderSnapshotMark[loc]==stamp)ladderSnapshotMark[loc]=0;
            }
            ladderSnapshotChangedCount[depth]=0;
            // This branch has been rewound to the position that existed before
            // the move saved at depth.  No child snapshot may remain active.
            ladderSnapshotActiveDepth=depth-1;
            ladderKoLoc=ladderKoAtDepth[depth];
        }

        private void RestoreLadderPath(int depth)
        {
            // A child snapshot stores only the changes made after that child
            // was entered.  Restore all still-active descendants before the
            // parent snapshot; restoring only the parent would leave child
            // mutations on ladderBoard and make later branches order-dependent.
            while(ladderSnapshotActiveDepth>depth)
                RestoreLadderSnapshot(ladderSnapshotActiveDepth);
            RestoreLadderSnapshot(depth);
        }

        private bool CanPlayLadderMove(int loc,int color)
        {
            int savedKo=ladderKoLoc;
            ladderTrialActive=true;ladderTrialChangedCount=0;activeBoard=ladderBoard;
            bool legal=PlayLadderMove(loc,color);
            RestoreLadderTrial();
            activeBoard=ladderBoard;ladderKoLoc=savedKo;
            return legal;
        }

        private bool PlayLadderMove(int loc,int color)
        {
            if(loc<0||loc>=GoGame.AREA||activeBoard[loc]!=GoGame.EMPTY||loc==ladderKoLoc)return false;
            bool trial=ladderTrialActive;
            bool snapshot=!trial&&ladderSnapshotActiveDepth>=0;
            bool rootMove=!trial&&!snapshot&&ladderAttemptActive;
            if(rootMove)
            {
                // Reuse the trial journal as a move-local rollback journal;
                // mutations are also recorded in the enclosing attempt journal
                // by SetLadderValue, so a legal root move remains reversible
                // without copying all 361 board points.
                ladderTrialActive=true;ladderTrialChangedCount=0;trial=true;
            }
            int oldKo=ladderKoLoc;SetLadderValue(loc,color);int captured=0,single=GoGame.NONE;
            ladderMoveCaptureGeneration++;
            if(ladderMoveCaptureGeneration<=0)
            {
                for(int i=0;i<GoGame.AREA;i++)ladderMoveCaptureStamp[i]=0;
                ladderMoveCaptureGeneration=1;
            }
            int n=Left(loc);if(n>=0&&activeBoard[n]==-color)captured+=CaptureLadderGroup(n,-color,ref single);
            n=Right(loc);if(n>=0&&activeBoard[n]==-color)captured+=CaptureLadderGroup(n,-color,ref single);
            n=Up(loc);if(n>=0&&activeBoard[n]==-color)captured+=CaptureLadderGroup(n,-color,ref single);
            n=Down(loc);if(n>=0&&activeBoard[n]==-color)captured+=CaptureLadderGroup(n,-color,ref single);
            int ownSize=CollectGroup(loc,color);int ownLibs=CountLiberties();
            if(ownLibs==0)
            {
                if(trial)RestoreLadderTrial();
                else if(snapshot)RestoreLadderSnapshot(ladderSnapshotActiveDepth);
                ladderKoLoc=oldKo;return false;
            }
            if(rootMove)CommitLadderTrial();
            ladderKoLoc=GoGame.NONE;if(captured==1&&ownSize==1&&ownLibs==1)ladderKoLoc=single;return true;
        }

        private int CaptureLadderGroup(int start,int color,ref int single)
        {
            if(activeBoard[start]!=color||ladderMoveCaptureStamp[start]==ladderMoveCaptureGeneration)return 0;
            int size=CollectGroup(start,color);
            for(int i=0;i<size;i++)ladderMoveCaptureStamp[groupMembers[i]]=ladderMoveCaptureGeneration;
            if(CountLiberties()!=0)return 0;
            single=size==1?groupMembers[0]:GoGame.NONE;
            for(int i=0;i<size;i++)SetLadderValue(groupMembers[i],GoGame.EMPTY);
            return size;
        }

        private void SetLadderValue(int loc,int value)
        {
            if(ladderTrialActive)
            {
                RecordLadderAttemptMutation(loc);
                if(!ladderTrialMarked[loc])
                {
                    ladderTrialMarked[loc]=true;
                    ladderTrialChangedLoc[ladderTrialChangedCount]=loc;
                    ladderTrialOldValue[ladderTrialChangedCount]=activeBoard[loc];
                    ladderTrialChangedCount++;
                }
                activeBoard[loc]=value;
                return;
            }
            if(ladderSnapshotActiveDepth>=0)
            {
                RecordLadderAttemptMutation(loc);
                int depth=ladderSnapshotActiveDepth;
                int stamp=ladderSnapshotStamp[depth];
                if(ladderSnapshotMark[loc]!=stamp)
                {
                    ladderSnapshotMark[loc]=stamp;
                    int offset=depth*GoGame.AREA+ladderSnapshotChangedCount[depth];
                    ladderSnapshots[offset]=loc;
                    ladderSnapshots[offset+MAX_LADDER_DEPTH*GoGame.AREA]=activeBoard[loc];
                    ladderSnapshotChangedCount[depth]++;
                }
            }
            else RecordLadderAttemptMutation(loc);
            activeBoard[loc]=value;
        }

        private void BeginLadderAttempt()
        {
            if(ladderAttemptActive)EndLadderAttempt();
            ladderChangeGeneration++;
            if(ladderChangeGeneration<=0)
            {
                for(int i=0;i<GoGame.AREA;i++)ladderChangedStamp[i]=0;
                ladderChangeGeneration=1;
            }
            ladderChangedCount=0;ladderAttemptActive=true;
            ladderSnapshotActiveDepth=-1;
        }

        private void RecordLadderAttemptMutation(int loc)
        {
            if(!ladderAttemptActive||loc<0||loc>=GoGame.AREA)return;
            if(ladderChangedStamp[loc]==ladderChangeGeneration)return;
            if(ladderChangedCount>=ladderChangedLocations.Length)return;
            ladderChangedStamp[loc]=ladderChangeGeneration;
            ladderChangedLocations[ladderChangedCount]=loc;
            ladderChangedOriginal[ladderChangedCount]=activeBoard[loc];
            ladderChangedCount++;
        }

        private void EndLadderAttempt()
        {
            while(ladderSnapshotActiveDepth>=0)
                RestoreLadderSnapshot(ladderSnapshotActiveDepth);
            if(ladderAttemptActive)
            {
                for(int i=ladderChangedCount-1;i>=0;i--)
                {
                    int loc=ladderChangedLocations[i];
                    RestoreLadderPoint(loc,ladderChangedOriginal[i]);
                }
            }
            ladderChangedCount=0;ladderAttemptActive=false;
            ladderSnapshotActiveDepth=-1;ladderTrialActive=false;
        }

        private void CommitLadderTrial()
        {
            for(int i=0;i<ladderTrialChangedCount;i++)
                ladderTrialMarked[ladderTrialChangedLoc[i]]=false;
            ladderTrialChangedCount=0;ladderTrialActive=false;
        }

        private void RestoreLadderTrial()
        {
            for(int i=ladderTrialChangedCount-1;i>=0;i--)
            {
                int loc=ladderTrialChangedLoc[i];
                RestoreLadderPoint(loc,ladderTrialOldValue[i]);
                ladderTrialMarked[loc]=false;
            }
            ladderTrialChangedCount=0;
            ladderTrialActive=false;
        }

        private void RestoreLadderPoint(int loc,int value)
        {
            ladderBoard[loc]=value;
        }
    }
}
