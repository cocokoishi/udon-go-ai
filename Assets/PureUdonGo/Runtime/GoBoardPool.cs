using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Synchronized pre-generated Go table pool. Only the visible table count
    /// is network state; every table retains its own GoGame, search tree,
    /// encoder, GPU runtime and async reader. A measured frame budget assigns
    /// fair scheduler slots to active table searches.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class GoBoardPool : UdonSharpBehaviour
    {
        // The production room intentionally presents exactly three tables.
        // The generated 16-slot backing pool remains intact for serialized
        // compatibility and future recovery, but table-count mutations are
        // disabled so the player-facing world is a fixed-size room.
        public const int FIXED_VISIBLE_TABLES=3;
        public const int MINIMUM_VISIBLE_TABLES=FIXED_VISIBLE_TABLES;
        public const int INITIAL_VISIBLE_TABLES=FIXED_VISIBLE_TABLES;
        public const int MAX_TABLES=16;

        private const float MAXIMUM_SUBMIT_FRAME_FRACTION=0.65f;
        private const float MINIMUM_SUBMIT_BUDGET_MS=0.50f;
        private const float MAXIMUM_SUBMIT_BUDGET_MS=6.00f;

        [UdonSynced] public int visibleTableCount=INITIAL_VISIBLE_TABLES;
        [UdonSynced] public int revision;

        public GameObject[] tableRoots;
        public GoUI primaryUI;
        // Generated references avoid recursive hierarchy searches in Udon Update.
        public GoGame[] games;
        public GoBoardView[] views;
        public GoUI[] uis;
        public GoAiController[] controllers;
        public bool profilingEnabled;
        public bool diagnosticsEnabled;

        [System.NonSerialized] public int localRunningSearches;
        [System.NonSerialized] public int localDispatchesPerFrame=1;
        [System.NonSerialized] public float localObservedFps=60f;
        [System.NonSerialized] public float localGpuFrameBudgetMs=1.5f;
        [System.NonSerialized] public int tableCapacity;
        [System.NonSerialized] public int allocatedSearchWorkers;
        [System.NonSerialized] public int allocatedEdgeCapacity;
        [System.NonSerialized] public int estimatedSearchArrayBytes;
        [System.NonSerialized] public int estimatedGpuTextureBytes;
        [System.NonSerialized] public int memoryAccountingRevision;
        public int moveMaskWarmupQuota=24;
        [System.NonSerialized] public int moveMaskWarmupPointsThisFrame;
        [System.NonSerialized] public int moveMaskWarmupFrames;
        [System.NonSerialized] public int maxMoveMaskWarmupPointsOneFrame;
        // A position revision can invalidate the authoritative hover cache
        // while an AI search owns the same table. Keep the pending bit so the
        // cache resumes on the next human turn, but prove that no rule probes
        // were spent during the AI-critical window.
        [System.NonSerialized] public int moveMaskWarmupPointsDuringAiSearch;
        [System.NonSerialized] public int moveMaskWarmupFramesDuringAiSearch;
        [System.NonSerialized] public int moveMaskWarmupSuppressedAiFrames;
        [System.NonSerialized] public int moveMaskWarmupSuppressedCriticalFrames;
        // A small time slice spreads exact legality probes across rendered
        // frames. moveMaskWarmupQuota remains a point safety cap for coarse
        // clocks; it is no longer the primary pacing mechanism.
        public float moveMaskWarmupMillisecondsPerFrame=GoGame.MOVE_MASK_WARMUP_DEFAULT_MS;
        [System.NonSerialized] public float moveMaskWarmupMillisecondsThisFrame;
        [System.NonSerialized] public float maxMoveMaskWarmupMillisecondsOneFrame;
        // Fixed local ring buffer used by the performance verifier to derive
        // frame-tail statistics. It is not synchronized and never drives
        // gameplay decisions.
        public float[] frameMillisecondsSamples=new float[2048];
        public int frameSampleCount;
        public int frameSampleWriteIndex;
        public float lastObservedFrameMilliseconds;
        public float maxObservedFrameMilliseconds;

        private int appliedVisibleTableCount=-1;
        private int nextSchedulerFrame;
        private int nextMemoryAccountingFrame;
        private float smoothedFrameMilliseconds=16.67f;
        private bool hasFrameTimingSample;
        private int nextMoveMaskWarmTable;
        private float smoothedGpuSubmitMilliseconds;
        private bool hasGpuSubmitSample;
        private bool referencesCached;
        private bool schedulerAwake=true;
        private int pendingMoveMaskTables;

        public void Start()
        {
            CacheReferences();
            if(IsLocalOwner()&&revision==0)
            {
                revision=1;
                RequestSerialization();
            }
            ApplyVisibility();
            RefreshGpuSchedule();
            if(diagnosticsEnabled)RefreshMemoryAccounting();
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
        }

        public void Update()
        {
            if(profilingEnabled)RecordFrameSample();
            if(pendingMoveMaskTables!=0)StepRoomMoveMaskWarmup();
            else
            {
                moveMaskWarmupPointsThisFrame=0;
                moveMaskWarmupMillisecondsThisFrame=0f;
            }
            if(schedulerAwake&&Time.frameCount>=nextSchedulerFrame)
            {
                nextSchedulerFrame=Time.frameCount+4;
                RefreshGpuSchedule();
            }
            if(diagnosticsEnabled&&Time.frameCount>=nextMemoryAccountingFrame)
            {
                nextMemoryAccountingFrame=Time.frameCount+60;
                RefreshMemoryAccounting();
            }
        }

        private void RecordFrameSample()
        {
            if(frameMillisecondsSamples==null||frameMillisecondsSamples.Length==0)return;
            float milliseconds=Time.unscaledDeltaTime*1000f;
            if(milliseconds<0f)return;
            lastObservedFrameMilliseconds=milliseconds;
            if(milliseconds>maxObservedFrameMilliseconds)maxObservedFrameMilliseconds=milliseconds;
            frameMillisecondsSamples[frameSampleWriteIndex]=milliseconds;
            frameSampleWriteIndex=(frameSampleWriteIndex+1)%frameMillisecondsSamples.Length;
            if(frameSampleCount<frameMillisecondsSamples.Length)frameSampleCount++;
        }

        public void ResetFrameSamples()
        {
            profilingEnabled=true;
            frameSampleCount=0;frameSampleWriteIndex=0;
            lastObservedFrameMilliseconds=0f;maxObservedFrameMilliseconds=0f;
        }

        public void StopFrameSamples(){profilingEnabled=false;}

        public void NotifySearchWork()
        {
            schedulerAwake=true;
            nextSchedulerFrame=0;
        }

        public void WakeMoveMaskTable(int index)
        {
            if(index<0||index>=GetClampedVisibleTableCount())return;
            pendingMoveMaskTables|=1<<index;
        }

        /// <summary>
        /// Performs at most one bounded, time-sliced legality slice for the
        /// whole room. The cursor is advanced after a selected table so three
        /// visible boards share one deterministic budget instead of each
        /// running a private batch in the same frame.
        /// </summary>
        private void StepRoomMoveMaskWarmup()
        {
            moveMaskWarmupPointsThisFrame=0;
            moveMaskWarmupMillisecondsThisFrame=0f;
            int capacity=GetTableCapacity();
            if(capacity<=0)return;
            // Move-mask warm-up is presentation-only. If any visible table is
            // in an AI turn, graph/readback pipeline, or hint search, keep the
            // room-wide background job asleep instead of stacking a second CPU
            // budget on top of the admitted controller frame. The pending bit
            // remains set and the next human-turn revision wakes it again.
            if(HasCriticalRoomWork())
            {
                moveMaskWarmupSuppressedCriticalFrames++;
                return;
            }
            // The room-wide budget is intentionally hard-capped. A table
            // cannot bypass the cooperative legality gate by serializing a
            // larger quota into the generated pool asset.
            int quota=Mathf.Clamp(moveMaskWarmupQuota,1,24);
            int start=Mathf.Clamp(nextMoveMaskWarmTable,0,capacity-1);
            for(int offset=0;offset<capacity;offset++)
            {
                int index=(start+offset)%capacity;
                if(index>=GetClampedVisibleTableCount())continue;
                if((pendingMoveMaskTables&(1<<index))==0)continue;
                GoGame game=GetGame(index);
                GoBoardView view=GetTableView(index);
                if(game==null||view==null||!view.moveMaskWarmupManagedByPool||
                    game.IsMoveMaskCacheCompleteForCurrentRevision())
                {pendingMoveMaskTables&=~(1<<index);continue;}
                GoAiController controller=GetAiController(index);
                bool aiTurn=game.matchStarted&&game.gameState==GoGame.STATE_PLAYING&&
                    game.IsAIControlled(game.sideToMove);
                // SearchState owns an independent exact move-mask cache. The
                // authoritative GoGame cache is presentation/hover work and
                // must never compete with an AI leaf or a human hint search.
                if(!CanWarmMoveMask(game,controller))
                {
                    if(aiTurn)moveMaskWarmupSuppressedAiFrames++;
                    continue;
                }
                float startedAt=Time.realtimeSinceStartup;
                int advanced=game.StepMoveMaskCacheTimeSliced(
                    moveMaskWarmupMillisecondsPerFrame,quota);
                if(game.IsMoveMaskCacheCompleteForCurrentRevision())
                    pendingMoveMaskTables&=~(1<<index);
                moveMaskWarmupPointsThisFrame=Mathf.Max(0,advanced);
                if(aiTurn&&advanced>0)
                {
                    moveMaskWarmupPointsDuringAiSearch+=advanced;
                    moveMaskWarmupFramesDuringAiSearch++;
                }
                moveMaskWarmupFrames++;
                float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
                moveMaskWarmupMillisecondsThisFrame=elapsed;
                if(elapsed>maxMoveMaskWarmupMillisecondsOneFrame)
                    maxMoveMaskWarmupMillisecondsOneFrame=elapsed;
                if(moveMaskWarmupPointsThisFrame>maxMoveMaskWarmupPointsOneFrame)
                    maxMoveMaskWarmupPointsOneFrame=moveMaskWarmupPointsThisFrame;
                nextMoveMaskWarmTable=(index+1)%capacity;
                return;
            }
            nextMoveMaskWarmTable=(start+1)%capacity;
        }

        private bool HasCriticalRoomWork()
        {
            int visible=GetClampedVisibleTableCount();
            for(int i=0;i<visible&&i<GetTableCapacity();i++)
            {
                GoGame game=GetGame(i);
                GoAiController controller=GetAiController(i);
                if(controller!=null&&controller.HasCriticalSearchWork())return true;
                if(game!=null&&game.matchStarted&&game.gameState==GoGame.STATE_PLAYING&&
                    game.IsAIControlled(game.sideToMove))return true;
            }
            return false;
        }

        private bool CanWarmMoveMask(GoGame game,GoAiController controller)
        {
            if(game==null)return false;
            if(game.IsMoveMaskWarmupDeferred())return false;
            if(game.matchStarted&&game.gameState==GoGame.STATE_PLAYING&&
                game.IsAIControlled(game.sideToMove))return false;
            if(controller!=null&&controller.HasCriticalSearchWork())return false;
            return true;
        }

        public override void OnDeserialization()
        {
            ApplyVisibility();
            RefreshGpuSchedule();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ApplyVisibility();
            RefreshGpuSchedule();
            if(player!=null&&player.isLocal)
                SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            ApplyVisibility();
            if(IsLocalOwner())SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),30);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            ApplyVisibility();
            if(IsLocalOwner())SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),4);
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),30);
        }

        public void RecoverNetworkState()
        {
            if(!IsLocalOwner())
            {
                ApplyVisibility();
                return;
            }
            if(!TakeOwnership())
            {
                ApplyVisibility();
                return;
            }
            int clamped=GetClampedVisibleTableCount();
            if(clamped!=visibleTableCount)
            {
                visibleTableCount=clamped;
                revision++;
                RequestSerialization();
            }
            ApplyVisibility();
        }

        public void ApplyVisibility()
        {
            CacheReferences();
            int count=GetClampedVisibleTableCount();
            bool countChanged=count!=appliedVisibleTableCount;
            appliedVisibleTableCount=count;
            if(!countChanged)return;
            if(tableRoots!=null)
            {
                for(int i=0;i<tableRoots.Length;i++)
                {
                    bool visible=i<count;
                    GoAiController controller=GetAiController(i);
                    if(tableRoots[i]!=null)
                    {
                        if(!visible&&tableRoots[i].activeSelf&&controller!=null)
                            controller.ReleaseHiddenTableResources();
                        tableRoots[i].SetActive(visible);
                    }
                    if(controller!=null)controller.schedulerEnabled=visible;
                }
            }
            // Hidden pre-generated tables are completely inactive. Do not
            // walk their UI/TMP trees during a visibility change; they must
            // not contribute refresh work until they become visible.
            for(int i=0;i<count&&i<GetTableCapacity();i++)
            {
                GoUI tableUI=GetTableUI(i);
                if(tableUI!=null)tableUI.RefreshNow();
            }
            RefreshGpuSchedule();
            pendingMoveMaskTables=(1<<count)-1;
            if(diagnosticsEnabled)RefreshMemoryAccounting();
        }

        public void RefreshMemoryAccounting()
        {
            tableCapacity=GetTableCapacity();allocatedSearchWorkers=0;
            allocatedEdgeCapacity=0;estimatedSearchArrayBytes=0;
            estimatedGpuTextureBytes=0;
            if(tableRoots!=null)
            {
                for(int i=0;i<tableRoots.Length;i++)
                {
                    GoAiController controller=GetAiController(i);
                    if(controller==null)continue;
                    int workerBytes=0;
                    if(controller.search!=null)
                    {
                        workerBytes+=controller.search.GetEstimatedAllocatedArrayBytes();
                        if(controller.search.simulationState!=null)
                            workerBytes+=controller.search.simulationState.GetEstimatedAllocatedArrayBytes();
                        if(controller.search.edgeChild!=null)
                            allocatedEdgeCapacity+=controller.search.edgeChild.Length;
                    }
                    if(controller.encoder!=null)
                        workerBytes+=controller.encoder.GetEstimatedAllocatedArrayBytes();
                    int gpuBytes=controller.runtime==null?0:
                        controller.runtime.GetEstimatedGpuTextureBytes();
                    estimatedSearchArrayBytes+=workerBytes;
                    estimatedGpuTextureBytes+=gpuBytes;
                    if((controller.search!=null&&(controller.search.nodeStorageAllocated||
                        controller.search.edgeStorageAllocated))||
                        (controller.encoder!=null&&controller.encoder.ladderStorageAllocated)||
                        gpuBytes>0)allocatedSearchWorkers++;
                }
            }
            memoryAccountingRevision++;
            if(memoryAccountingRevision==0)memoryAccountingRevision=1;
        }

        public void RefreshGpuSchedule()
        {
            CacheReferences();
            if(tableRoots==null)return;
            float observed=Time.unscaledDeltaTime*1000f;
            if(observed>=3f&&observed<=100f)
            {
                if(!hasFrameTimingSample){smoothedFrameMilliseconds=observed;hasFrameTimingSample=true;}
                else smoothedFrameMilliseconds+=(observed-smoothedFrameMilliseconds)*0.12f;
            }
// `visibleTableCount` is synchronized for legacy scene recovery, but the
// shipped room is intentionally fixed at three visible tables.  Never let a
// malformed/old snapshot re-enable hidden controllers in the scheduler.
int visibleCount=GetClampedVisibleTableCount();
            int running=0;
            float submitTotal=0f;
            int submitSamples=0;
            for(int i=0;i<visibleCount&&i<tableRoots.Length;i++)
            {
                GoAiController controller=GetAiController(i);
                if(controller==null)continue;
                GoGame game=GetGame(i);
                bool active=(controller.search!=null&&controller.search.phase!=GoMctsSearch.PHASE_IDLE&&controller.search.phase!=GoMctsSearch.PHASE_CANCELLED)||
                    controller.hintRequested||controller.controllerState==GoAiController.STATE_THINKING||
                    (game!=null&&game.matchStarted&&game.gameState==GoGame.STATE_PLAYING&&game.IsAIControlled(game.sideToMove));
                if(!active)continue;
                running++;
                // Active scheduling must use the graph's live submit EMA.
                // A completed-search wall-time statistic is not a valid
                // estimate for the currently running graph and can under-
                // estimate a dense position badly.
                float liveSubmit=controller.runtime==null?0f:
                    (controller.runtime.smoothedGpuSubmitMilliseconds>0f?
                        controller.runtime.smoothedGpuSubmitMilliseconds:
                        controller.runtime.persistentGraphSubmitEmaMs);
                if(liveSubmit<=0f)liveSubmit=controller.smoothedGpuSubmitMilliseconds;
                if(liveSubmit>0f)
                {
                    submitTotal+=liveSubmit;
                    submitSamples++;
                    if(!hasGpuSubmitSample)
                    {
                        smoothedGpuSubmitMilliseconds=liveSubmit;
                        hasGpuSubmitSample=true;
                    }
                    else smoothedGpuSubmitMilliseconds+=
                        (liveSubmit-smoothedGpuSubmitMilliseconds)*0.20f;
                }
            }
            localRunningSearches=running;
            // Run once when activity ends to clear slots, then sleep until a
            // game/controller lifecycle event announces more work.
            schedulerAwake=running>0;
            float fps=Mathf.Clamp(1000f/Mathf.Clamp(smoothedFrameMilliseconds,3.333f,33.333f),30f,300f);
            float average=submitSamples>0?submitTotal/submitSamples:
                (hasGpuSubmitSample?smoothedGpuSubmitMilliseconds:0f);
            int dispatches=CalculateDispatchCapacity(fps,average,Mathf.Max(1,running));
            localObservedFps=fps;
            localDispatchesPerFrame=dispatches;
            localGpuFrameBudgetMs=CalculateFrameSubmitBudget(fps);
            int stride=running<=1?1:Mathf.Clamp(Mathf.CeilToInt((float)running/dispatches),1,MAX_TABLES);
            int rank=0;
            for(int i=0;i<tableRoots.Length;i++)
            {
                GoAiController controller=GetAiController(i);
                if(controller==null)continue;
                bool visible=i<visibleCount;
                controller.tableIdentity=i;
                controller.schedulerEnabled=visible;
                controller.schedulerActiveSearches=Mathf.Max(1,running);
                controller.schedulerDispatchesPerFrame=dispatches;
                if(visible&&IsControllerActive(i))
                {
                    controller.schedulerStride=stride;
                    controller.schedulerSlot=rank++;
                }
                else
                {
                    controller.schedulerStride=1;
                    controller.schedulerSlot=0;
                }
                controller.schedulerObservedFps=fps;
                // The GPU submit budget is also the upper bound from which a
                // controller derives its cooperative CPU/search slice. Keep
                // this separate from dispatch admission so a granted table
                // cannot execute an unbounded legality or MCTS transition.
                float perDispatchBudget=localGpuFrameBudgetMs/
                    Mathf.Max(1,dispatches);
                controller.schedulerWorkBudgetMs=Mathf.Clamp(perDispatchBudget*0.75f,
                    0.25f,4.00f);
                if(controller.runtime!=null)
                    controller.runtime.maxGpuSubmitMillisecondsPerFrame=
                        Mathf.Clamp(perDispatchBudget,0.25f,6.00f);
            }
        }

        public int CalculateDispatchCapacity(float framesPerSecond,float averageOneCycleSubmitMilliseconds,int runningSearches)
        {
            float fps=Mathf.Clamp(framesPerSecond,30f,300f);
            int refreshCapacity=Mathf.Clamp(Mathf.CeilToInt(fps/60f),1,5);
            float budget=CalculateFrameSubmitBudget(fps);
            int measuredCapacity=1;
            if(averageOneCycleSubmitMilliseconds>0.0001f)
                measuredCapacity=Mathf.Clamp(Mathf.FloorToInt(budget*0.98f/averageOneCycleSubmitMilliseconds),1,5);
            return Mathf.Clamp(Mathf.Min(refreshCapacity,measuredCapacity),1,Mathf.Max(1,runningSearches));
        }

        public float CalculateFrameSubmitBudget(float framesPerSecond)
        {
            float fps=Mathf.Clamp(framesPerSecond,30f,300f);
            return Mathf.Clamp((1000f/fps)*MAXIMUM_SUBMIT_FRAME_FRACTION,MINIMUM_SUBMIT_BUDGET_MS,MAXIMUM_SUBMIT_BUDGET_MS);
        }

        private bool IsControllerActive(int index)
        {
            if(index<0||tableRoots==null||index>=tableRoots.Length)return false;
            GoAiController controller=GetAiController(index);
            GoGame game=GetGame(index);
            if(controller==null)return false;
            return (controller.search!=null&&controller.search.phase!=GoMctsSearch.PHASE_IDLE&&controller.search.phase!=GoMctsSearch.PHASE_CANCELLED)||
                controller.hintRequested||controller.controllerState==GoAiController.STATE_THINKING||
                (game!=null&&game.matchStarted&&game.gameState==GoGame.STATE_PLAYING&&game.IsAIControlled(game.sideToMove));
        }

        private int GetTableCapacity()
        {
            if(tableRoots==null)return 0;
            return Mathf.Min(tableRoots.Length,MAX_TABLES);
        }

        private GoGame GetGame(int index)
        {
            CacheReferences();
            return games!=null&&index>=0&&index<games.Length?games[index]:null;
        }

        private GoUI GetTableUI(int index)
        {
            CacheReferences();
            return uis!=null&&index>=0&&index<uis.Length?uis[index]:null;
        }

        private GoBoardView GetTableView(int index)
        {
            CacheReferences();
            return views!=null&&index>=0&&index<views.Length?views[index]:null;
        }

        private GoAiController GetAiController(int index)
        {
            CacheReferences();
            return controllers!=null&&index>=0&&index<controllers.Length?controllers[index]:null;
        }

        private void CacheReferences()
        {
            if(referencesCached||tableRoots==null)return;
            int count=tableRoots.Length;
            if(games==null||games.Length!=count)games=new GoGame[count];
            if(views==null||views.Length!=count)views=new GoBoardView[count];
            if(uis==null||uis.Length!=count)uis=new GoUI[count];
            if(controllers==null||controllers.Length!=count)controllers=new GoAiController[count];
            for(int i=0;i<count;i++)
            {
                GameObject table=tableRoots[i];if(table==null)continue;
                if(games[i]==null)games[i]=table.GetComponentInChildren<GoGame>(true);
                if(views[i]==null)views[i]=table.GetComponentInChildren<GoBoardView>(true);
                if(uis[i]==null)uis[i]=table.GetComponentInChildren<GoUI>(true);
                if(controllers[i]==null)controllers[i]=table.GetComponentInChildren<GoAiController>(true);
            }
            referencesCached=true;
        }

        private int GetMinimumVisibleTableCount()
        {
            return Mathf.Min(MINIMUM_VISIBLE_TABLES,GetTableCapacity());
        }

        private int GetClampedVisibleTableCount()
        {
            int capacity=GetTableCapacity();
            if(capacity<=0)return 0;
            return Mathf.Clamp(FIXED_VISIBLE_TABLES,GetMinimumVisibleTableCount(),capacity);
        }

        private bool IsLocalOwner()
        {
            return !Utilities.IsValid(Networking.LocalPlayer)||Networking.IsOwner(gameObject);
        }

        private bool TakeOwnership()
        {
            if(!Utilities.IsValid(Networking.LocalPlayer))return true;
            if(!Networking.IsOwner(gameObject))Networking.SetOwner(Networking.LocalPlayer,gameObject);
            return Networking.IsOwner(gameObject);
        }
    }
}
