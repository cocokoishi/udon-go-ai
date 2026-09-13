using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Product-level AI loop. It drives the verified GPU graph and PUCT core
    /// as a state machine: an admitted Update owns one cooperative deadline and
    /// drains CPU-only transitions until that deadline, with serialized
    /// asynchronous final-output readback as the normal cross-frame boundary.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class GoAiController : UdonSharpBehaviour
    {
        public const int MODE_PVP=0;
        public const int MODE_PVAI=1;
        public const int MODE_AIVP=2;
        public const int MODE_AIVAI=3;
        public const int STATE_IDLE=0;
        public const int STATE_THINKING=1;
        public const int STATE_ERROR=2;

        public GoGame game;
        public GoBoardPool boardPool;
        public GoAiSettings aiSettings;
        public GoDifficultyProfile difficulty;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoFeatureEncoder encoder;
        public GoGpuNeuralRuntime runtime;
        public GoGpuNeuralOutputReader reader;
        // A cancelled VRCAsyncGPUReadback has no cancellation API and a
        // platform failure (notably ClientSim's unsupported format path) may
        // never deliver the stale callback. Keep one pre-generated reader in
        // reserve so a new owner can continue without reusing that callback
        // channel. The old reader remains quarantined and still rejects any
        // late callback it eventually receives.
        public GoGpuNeuralOutputReader alternateReader;
        // A third pre-generated channel keeps the search live when both the
        // active reader and the first reserve have unresolved callbacks.
        // Readers are never instantiated at runtime.
        public GoGpuNeuralOutputReader tertiaryReader;
        // Stable pool identities. The legacy fields above remain serialized
        // compatibility aliases, while rotation always uses A/B/C so a
        // recovered reader can re-enter service after its late callback.
        public GoGpuNeuralOutputReader readerA;
        public GoGpuNeuralOutputReader readerB;
        public GoGpuNeuralOutputReader readerC;
        public int activeReaderIndex;
        public int recoveredReaderSelections;
        public GoMctsSearch search;
        public GoBoardView view;
        public GoUI ui;
        public GoTelemetry telemetry;
        // Assigned by GoBoardPool.  The default keeps the single-table
        // product path unchanged; pooled tables receive a fair frame slot so
        // a heavy KataGo graph cannot monopolize every Update.
        [System.NonSerialized] public bool schedulerEnabled;
        [System.NonSerialized] public int schedulerStride=1;
        [System.NonSerialized] public int schedulerSlot;
        [System.NonSerialized] public int schedulerActiveSearches;
        [System.NonSerialized] public int schedulerDispatchesPerFrame=1;
        [System.NonSerialized] public float schedulerObservedFps=60f;
        [System.NonSerialized] public float schedulerWorkBudgetMs=1.50f;
        [System.NonSerialized] public float grantedFrameWorkMs;
        [System.NonSerialized] public float usedFrameWorkMs;
        [System.NonSerialized] public float maxFrameWorkOvershootMs;
        [System.NonSerialized] public float grantedGpuBudgetMs;
        [System.NonSerialized] public float usedGpuSubmitMs;
        [System.NonSerialized] public int sameFramePhaseTransitions;
        [System.NonSerialized] public int avoidableYieldCount;
        [System.NonSerialized] public int deadlineYieldCount;
        [System.NonSerialized] public int asyncReadbackWaitFrames;
        [System.NonSerialized] public int telemetryCommitsDuringSearch;
        [System.NonSerialized] public int telemetrySerializationsDuringSearch;
        [System.NonSerialized] public int searchFrameCount;
        [System.NonSerialized] public int visit0CompletionFrame=-1;
        [System.NonSerialized] public int visit1CompletionFrame=-1;
        [System.NonSerialized] public int visit2CompletionFrame=-1;
        [System.NonSerialized] public int visit3CompletionFrame=-1;
        [System.NonSerialized] public int tableIdentity=-1;
        [System.NonSerialized] public int schedulerDispatchCount;
        [System.NonSerialized] public int schedulerSkipCount;
        [System.NonSerialized] public int schedulerLastDispatchFrame=-1;
        [System.NonSerialized] public int schedulerLastSkipFrame=-1;
        [UdonSynced] public int mode=MODE_PVAI;
        [UdonSynced] public int aiColor=GoGame.WHITE;
        public bool autoStart=true;
        public int ownerLifecycle;
        public int controllerState=STATE_IDLE;
        public int aiMoves;
        public string lastError="";
        public float lastNeuralScoreMean;
        public float lastNeuralScoreStdev;
        public float lastNeuralLead;
        public float lastNeuralOwnershipMean;
        public float lastNeuralBlackWinProbability;
        public float lastNeuralWhiteWinProbability;
        public float lastNeuralNoResultProbability;
        public float lastNeuralValue;
        public int lastNeuralOutputRevision;
        // Separate identity for the ownership presentation pass.  The score
        // head is decoded immediately, while ownership is converted over
        // several admitted frames and must not inherit the score revision.
        public int lastNeuralOwnershipOutputRevision;

        // Local root-output snapshot used by the developer consistency probe.
        // It is intentionally not synchronized: production networking publishes
        // compact telemetry, while the probe reads the exact first GPU result
        // before PUCT backs it up. This keeps the verification on the real
        // encoder -> shader -> async-readback path.
        public float[] lastRootPolicySpatial=new float[GoGame.AREA];
        public float lastRootPolicyPass;
        public float[] lastRootValue=new float[3];
        public float[] lastRootScore=new float[4];
        public float[] lastRootOwnership=new float[GoGame.AREA];
        public int lastRootOutputRevision;
        public int lastRootOutputSearchToken;
        public int lastRootReadbackStages;
        public int lastRootOwnershipReadbacks;
        public bool hintRequested;
        public int hintRootRevision;
        public int hintRootSettingsRevision;
        public int hintRootOwnerLifecycle;
        public int hintRootSide;

        // Last completed search timing breakdown. These values are local
        // runtime diagnostics (not network state) and are intentionally
        // public so ClientSim can read the actual Udon execution path.
        public int lastSearchTargetVisits;
        public int lastSearchCompletedVisits;
        public int lastSearchFrames;
        public float lastSearchMilliseconds;
        public float lastSearchSetupMilliseconds;
        public float lastSearchStepMilliseconds;
        public float lastSuperkoMilliseconds;
        public float lastFeatureMilliseconds;
        public float lastFeatureActiveCpuMilliseconds;
        public float lastLadderActiveCpuMilliseconds;
        public float lastGpuInputSetupMilliseconds;
        public float lastGpuUploadMilliseconds;
        public float lastGpuInputPackingMilliseconds;
        public float lastGpuSpatialApplyMilliseconds;
        public float lastGpuGlobalApplyMilliseconds;
        public float lastGpuInputBlitMilliseconds;
        public float lastGpuDispatchMilliseconds;
        public float lastGpuEvaluationMilliseconds;
        public float lastReadbackMilliseconds;
        public float lastNeuralSubmitMilliseconds;
        public float lastCommitMilliseconds;
        public int lastFeatureSteps;
        public int lastLadderStepCalls;
        public int lastLadderSearchTransitions;
        public int lastLadderNodes;
        public int lastGpuPasses;
        public int lastGpuGraphFrames;
        public int gpuGraphPassesThisFrame;
        public int maxGpuGraphPassesOneFrame;
        public int gpuGraphVisitedStageMask;
        public int gpuGraphStage;
        public int gpuGraphStageProgress;
        public float maxGpuSubmitMsOneFrame;
        public float smoothedGpuSubmitMilliseconds;
        public int lastReadbackRequests;
        public int lastReadbackStages;
        public int lastSearchStepCalls;
        public int lastLegalMoveChecks;
        public int lastSimulationPlayCalls;
        public int lastSuperkoScanLocations;
        public int lastSuperkoScanFrames;
        public int lastHistoryEntriesExamined;
        public int lastHistoryQueryCount;
        public int lastHistoryBucketHitCount;
        public int lastHistoryBucketMissCount;
        public int lastHistoryFullScanCount;
        public int lastHistoryMaxEntriesExamined;
        // Cooperative superko-mask telemetry. These counters deliberately use
        // the product acceptance terminology so runtime/ClientSim evidence can
        // distinguish a completed 361-point mask from a monolithic Tick.
        public int superkoProbePoints;
        public int superkoProbeFrames;
        public float superkoProbeMilliseconds;
        public int maxSuperkoPointsOneFrame;
        public float maxSuperkoFrameMilliseconds;
        public int currentSuperkoProbeIndex;
        public int completedSuperkoProbeMasks;
        public int cancelledSuperkoProbes;
        public int lastCancelledSuperkoProbeIndex;
        public bool superkoProbeActive;
        // Root ownership presentation is diagnostic work, not an input to
        // PUCT. Keep its 361 tanh conversions out of the readback Tick while
        // preserving the exact float accumulation order and result identity.
        public int ownershipTelemetryPoints;
        public int ownershipTelemetryFrames;
        public int maxOwnershipTelemetryPointsOneFrame;
        public float maxOwnershipTelemetryFrameMilliseconds;
        public bool ownershipTelemetryActive;
        public int readerRotations;
        public int readerPoolExhaustions;
        public int searchWatchdogTrips;
        public float lastTickMilliseconds;
        public float maxTickMilliseconds;
        public int tickSamples;
        public float lastRootCopyMilliseconds;
        public float lastNeuralExpansionMilliseconds;
        public float lastBackupMilliseconds;
        public float lastScoreUtilityMilliseconds;
        public float lastMctsSelectionMilliseconds;
        public float lastMctsExpansionMilliseconds;
        public float lastMctsBackupMilliseconds;
        public float lastFeatureSuperkoMilliseconds;
        public float lastFeatureClearMilliseconds;
        public float lastFeatureBaseMilliseconds;
        public float lastLadderMilliseconds;
        public float lastFeatureAreaMilliseconds;
        public float maxMctsStepMilliseconds;
        public bool heavyResourcesReleased;
        // Local per-table cold-start preparation. It never changes search
        // identity and only runs on the authoritative owner of this table.
        public bool resourceWarmupActive;
        public bool resourceWarmupComplete;
        public int resourceWarmupStage;
        public string resourceWarmupError="";
        // Match-start pipeline warm-up evidence.  These are local diagnostics
        // copied from GoGpuNeuralRuntime so ClientSim can prove that the real
        // graph and packed readback were exercised before BeginSearch.
        public int pipelineWarmupFrames;
        public int pipelineWarmupPasses;
        public float pipelineWarmupMilliseconds;
        public bool pipelineWarmupReadbackComplete;

        // Local-only pending mask. It is public for production diagnostics and
        // the serialized-Udon verifier; it is never synchronized.
        public bool[] pendingSuperkoMask=new bool[GoGame.AREA];
        private bool authorityKnown;
        private bool wasAuthority;
        private bool tickWakeRequested=true;
        public int idleTicksSkipped;
        private int previousMode;
        private int previousAiColor;
        private bool previousMatchStarted;
        private bool previousBlackIsAI;
        private bool previousWhiteIsAI;
        private int previousStarter;
        private float performanceSearchStartedAt;
        private int performanceSearchStartedFrame;
        private bool performanceSearchActive;
        private float nextTelemetryPublishTime;
        private float nextSurfaceRefreshTime;
        private bool superkoScanActive;
        private int superkoScanCursor;
        private int superkoScanToken=-1;
        private int superkoScanNode=-1;
        private int superkoScanRevision=-1;
        private int superkoScanGameSettingsRevision=-1;
        private int superkoScanSettingsRevision=-1;
        private int superkoScanOwnerLifecycle=-1;
        private int superkoScanHash0;
        private int superkoScanHash1;
        private int superkoScanHash2;
        private int superkoScanHash3;
        private float readerPoolWaitStartedAt;
        private float searchLastProgressAt;
        private int searchLastProgressVisits;
        private const int MIN_SUPERKO_LOCATIONS_PER_FRAME=32;
        private const int MAX_SUPERKO_LOCATIONS_PER_FRAME=256;
        private const float DEFAULT_WORK_BUDGET_MS=1.50f;
        private const float MIN_WORK_BUDGET_MS=0.25f;
        private const float MAX_WORK_BUDGET_MS=4.00f;
        private const float READER_POOL_WAIT_TIMEOUT_SECONDS=5.00f;
        private const float SEARCH_STALL_TIMEOUT_SECONDS=30.00f;
        private int lastSurfaceGameRevision=-1;
        private int lastSurfaceSearchPhase=-1;
        private int lastSurfaceControllerState=-1;
        private int lastPublishedGameRevision=-1;
        private int lastPublishedSearchPhase=-1;
        private int lastPublishedControllerState=-1;
        private int lastPublishedOwnerLifecycle=-1;
        private int accountedEncodeGeneration=-1;
        private float activeFrameDeadline;
        private bool activeFrameBudget;
        private int activePumpSafety;
        private bool ownershipTelemetryInitialized;
        private int ownershipTelemetryToken=-1;
        private int ownershipTelemetryNode=-1;
        private int ownershipTelemetryRevision=-1;
        private int ownershipTelemetrySettingsRevision=-1;
        private int ownershipTelemetryOwnerLifecycle=-1;
        private int ownershipTelemetrySide=GoGame.EMPTY;
        private int ownershipTelemetryHash0;
        private int ownershipTelemetryHash1;
        private int ownershipTelemetryHash2;
        private int ownershipTelemetryHash3;
        private int ownershipTelemetryCursor;
        private float ownershipTelemetrySum;
        // Immutable settings captured when the current SearchSession starts.
        // Do not read the live profile while that session is running: a
        // settings Apply affects the next search only.
        private int activeSearchTransitionsPerFrame=1;
        private float activeSearchResignThreshold=-1f;

        public void Start()
        {
            InitializeReaderPool();
            if(aiSettings==null&&game!=null)aiSettings=game.aiSettings;
            if(game!=null&&game.aiSettings==null&&aiSettings!=null)game.aiSettings=aiSettings;
            if(difficulty!=null)difficulty.aiController=this;
            if(blackDifficulty==null)blackDifficulty=difficulty;
            if(whiteDifficulty==null)whiteDifficulty=difficulty;
            if(blackDifficulty!=null)blackDifficulty.aiController=this;
            if(whiteDifficulty!=null)whiteDifficulty.aiController=this;
            if(aiSettings!=null)
            {
                aiSettings.game=game;aiSettings.aiController=this;
                aiSettings.RegisterProfiles(blackDifficulty,whiteDifficulty);
            }
            if(ownerLifecycle==0)ownerLifecycle=1;
            SyncControllerMirrorFromGame();
            previousMode=mode;previousAiColor=aiColor;
            if(game!=null){previousMatchStarted=game.matchStarted;previousBlackIsAI=game.blackIsAI;previousWhiteIsAI=game.whiteIsAI;previousStarter=game.starter;}
            RefreshSurfaces();
        }

        /// <summary>
        /// Requests local cold-start preparation for this table only. Non-
        /// owners and PvP-only tables do not allocate AI resources.
        /// </summary>
        public void RequestAiResourceWarmup()
        {
            if(game==null||!game.HasAnyAI()||!IsLocalGameOwner())return;
            if(resourceWarmupComplete||resourceWarmupActive)return;
            resourceWarmupActive=true;resourceWarmupStage=0;
            resourceWarmupError="";pipelineWarmupReadbackComplete=false;
            pipelineWarmupFrames=0;pipelineWarmupPasses=0;
            pipelineWarmupMilliseconds=0f;controllerState=STATE_THINKING;
            WakeForStateChange();
        }

        private bool IsLocalGameOwner()
        {
            VRCPlayerApi local=Networking.LocalPlayer;
            return !Utilities.IsValid(local)||
                (game!=null&&Networking.IsOwner(game.gameObject));
        }

        private bool StepResourceWarmup()
        {
            if(!resourceWarmupActive)return true;
            if(!IsLocalGameOwner())
            {
                if(runtime!=null)runtime.CancelPipelineWarmup();
                resourceWarmupActive=false;resourceWarmupComplete=false;
                resourceWarmupStage=0;pipelineWarmupReadbackComplete=false;
                return false;
            }

            // Stages 0..3 are allocation-only preparation. They deliberately
            // remain one stage per frame so a hidden/idle table never takes a
            // cold allocation spike in the first real AI tick.
            bool ok=true;
            if(resourceWarmupStage==0&&search!=null)
                ok=search.PrepareRuntimeStorage();
            else if(resourceWarmupStage==1&&encoder!=null)
                ok=encoder.PrepareRuntimeStorage();
            else if(resourceWarmupStage==2&&runtime!=null)
                ok=runtime.PrepareRuntimeResources();
            else if(resourceWarmupStage==3&&reader!=null)
                ok=reader.PrepareRuntimeResources();
            else if(resourceWarmupStage==4)
            {
                if(runtime==null)
                {
                    resourceWarmupError="GPU runtime reference missing";
                    resourceWarmupActive=false;resourceWarmupComplete=false;
                    SetError("AI resource warm-up failed: "+resourceWarmupError);
                    return false;
                }
                if(!runtime.pipelineWarmupActive&&
                    runtime.gpuGraphStage!=GoGpuNeuralRuntime.NN_READY_FOR_READBACK)
                {
                    if(!runtime.BeginPipelineWarmup(tableIdentity))
                    {
                        resourceWarmupError=runtime.pipelineWarmupError!=""?
                            runtime.pipelineWarmupError:runtime.lastError;
                        resourceWarmupActive=false;resourceWarmupComplete=false;
                        SetError("AI pipeline warm-up failed: "+resourceWarmupError);
                        return false;
                    }
                    controllerState=STATE_THINKING;
                    return false;
                }
                int graphState=runtime.pipelineWarmupActive?
                    runtime.StepPipelineWarmup(GetWorkBudgetMilliseconds()):
                    runtime.gpuGraphStage;
                pipelineWarmupFrames=runtime.pipelineWarmupFrames;
                pipelineWarmupPasses=runtime.pipelineWarmupPasses;
                pipelineWarmupMilliseconds=runtime.pipelineWarmupMilliseconds;
                if(graphState==GoGpuNeuralRuntime.NN_ERROR)
                {
                    resourceWarmupError=runtime.pipelineWarmupError!=""?
                        runtime.pipelineWarmupError:runtime.lastError;
                    resourceWarmupActive=false;resourceWarmupComplete=false;
                    SetError("AI pipeline warm-up failed: "+resourceWarmupError);
                    return false;
                }
                if(graphState!=GoGpuNeuralRuntime.NN_READY_FOR_READBACK)
                {
                    controllerState=STATE_THINKING;
                    return false;
                }
                resourceWarmupStage=5;
                return false;
            }
            else if(resourceWarmupStage==5)
            {
                if(reader==null||runtime==null)
                {
                    resourceWarmupError="pipeline warm-up reader/runtime reference missing";
                    resourceWarmupActive=false;resourceWarmupComplete=false;
                    SetError("AI pipeline warm-up failed: "+resourceWarmupError);
                    return false;
                }
                if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING)
                {
                    controllerState=STATE_THINKING;
                    return false;
                }
                if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_ERROR)
                {
                    resourceWarmupError=reader.lastError;
                    resourceWarmupActive=false;resourceWarmupComplete=false;
                    SetError("AI pipeline readback warm-up failed: "+resourceWarmupError);
                    return false;
                }
                if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_COMPLETE)
                {
                    reader.ReleaseResources();
                    runtime.ReleaseResources();
                    pipelineWarmupReadbackComplete=true;
                    pipelineWarmupFrames=runtime.pipelineWarmupFrames;
                    pipelineWarmupPasses=runtime.pipelineWarmupPasses;
                    pipelineWarmupMilliseconds=runtime.pipelineWarmupMilliseconds;
                    resourceWarmupStage=6;
                    resourceWarmupActive=false;
                    resourceWarmupComplete=true;
                    controllerState=STATE_IDLE;
                    resourceWarmupError="";
                    return true;
                }
                if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_QUARANTINED)
                {
                    if(!TryRotateReader())
                    {
                        controllerState=STATE_THINKING;
                        return false;
                    }
                    return false;
                }
                if(!reader.BeginAsyncReadback(runtime,runtime.pipelineWarmupToken,-1,
                    -1,-1,ownerLifecycle,0,0,0,0,true))
                {
                    resourceWarmupError=reader.lastError;
                    resourceWarmupActive=false;resourceWarmupComplete=false;
                    SetError("AI pipeline readback warm-up failed: "+resourceWarmupError);
                    return false;
                }
                controllerState=STATE_THINKING;
                return false;
            }
            else if(resourceWarmupStage>=6)
            {
                resourceWarmupActive=false;resourceWarmupComplete=true;
                controllerState=STATE_IDLE;
                return true;
            }
            if(!ok)
            {
                resourceWarmupError=search!=null&&search.lastError!=""?
                    search.lastError:encoder!=null&&encoder.lastError!=""?
                    encoder.lastError:runtime!=null&&runtime.lastError!=""?
                    runtime.lastError:reader!=null?reader.lastError:"warm-up reference missing";
                resourceWarmupActive=false;resourceWarmupComplete=false;
                SetError("AI resource warm-up failed: "+resourceWarmupError);
                return false;
            }
            resourceWarmupStage++;return false;
        }

        public override void OnDeserialization()
        {
            WakeForStateChange();
            bool controllerChanged=mode!=previousMode||aiColor!=previousAiColor;
            bool gameChanged=game!=null&&(game.matchStarted!=previousMatchStarted||game.blackIsAI!=previousBlackIsAI||game.whiteIsAI!=previousWhiteIsAI||game.starter!=previousStarter);
            if(controllerChanged||gameChanged)
            {
                SyncControllerMirrorFromGame();
                previousMode=mode;previousAiColor=aiColor;
                if(game!=null){previousMatchStarted=game.matchStarted;previousBlackIsAI=game.blackIsAI;previousWhiteIsAI=game.whiteIsAI;previousStarter=game.starter;}
                InvalidateSearch();lastError="";RefreshSurfaces();
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ownerLifecycle++;if(ownerLifecycle==0)ownerLifecycle=1;
            authorityKnown=true;wasAuthority=Networking.LocalPlayer!=null&&Networking.IsOwner(gameObject);
            hintRequested=false;
            InvalidateSearch();lastError="";RefreshSurfaces();
        }

        public void OnGameOwnershipChanged(){ownerLifecycle++;if(ownerLifecycle==0)ownerLifecycle=1;hintRequested=false;InvalidateSearch();}

        public void Update()
        {
            if(!NeedsPerFrameTick()){idleTicksSkipped++;return;}
            tickWakeRequested=false;
            float startedAt=Time.realtimeSinceStartup;
            if(performanceSearchActive)searchFrameCount++;
            Tick();
            lastTickMilliseconds=(Time.realtimeSinceStartup-startedAt)*1000f;
            if(lastTickMilliseconds>maxTickMilliseconds)maxTickMilliseconds=lastTickMilliseconds;
            tickSamples++;
        }

        public void WakeForStateChange()
        {
            tickWakeRequested=true;
            if(boardPool!=null)boardPool.NotifySearchWork();
        }

        /// <summary>
        /// Authoritative hover/move-mask work is presentation-only.  A table
        /// must expose one cheap query so GoBoardPool can suppress that work
        /// while any search, hint, graph submission or readback is critical.
        /// The lazy GetMoveMask path remains available for an actual click.
        /// </summary>
        public bool HasCriticalSearchWork()
        {
            if(hintRequested||controllerState==STATE_THINKING)return true;
            if(search!=null&&search.phase!=GoMctsSearch.PHASE_IDLE&&
                search.phase!=GoMctsSearch.PHASE_CANCELLED)return true;
            if(runtime!=null&&runtime.gpuGraphStage>=GoGpuNeuralRuntime.NN_INPUT_UPLOAD&&
                runtime.gpuGraphStage<=GoGpuNeuralRuntime.NN_READY_FOR_READBACK)return true;
            return reader!=null&&reader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING;
        }

        public bool NeedsPerFrameTick()
        {
            if(!autoStart)return false;
            if(tickWakeRequested)return true;
            if(resourceWarmupActive)return true;
            if(controllerState==STATE_ERROR)return false;
            if(!authorityKnown)return true;
            // Pending work still enters Tick's authority/revision checks on
            // every frame, before scheduler admission. Idle replicas sleep.
            if(hintRequested||controllerState==STATE_THINKING||superkoProbeActive||
                ownershipTelemetryActive)return true;
            if(search!=null&&search.phase!=GoMctsSearch.PHASE_IDLE&&
                search.phase!=GoMctsSearch.PHASE_CANCELLED)return true;
            if(runtime!=null&&runtime.gpuGraphStage>=GoGpuNeuralRuntime.NN_INPUT_UPLOAD&&
                runtime.gpuGraphStage<=GoGpuNeuralRuntime.NN_READY_FOR_READBACK)return true;
            if(reader!=null&&reader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING)
                return true;
            if(controllerState==STATE_ERROR)return false;
            return wasAuthority&&game!=null&&game.matchStarted&&
                game.gameState==GoGame.STATE_PLAYING&&game.IsAIControlled(game.sideToMove);
        }

        public void Tick()
        {
            if(!autoStart)return;
            if(game==null||difficulty==null||encoder==null||runtime==null||reader==null||search==null)
            {
                SetError("AI references are incomplete");return;
            }
            bool authority=Networking.LocalPlayer!=null&&Networking.IsOwner(game.gameObject);
            if(!authorityKnown||authority!=wasAuthority)
            {
                authorityKnown=true;wasAuthority=authority;ownerLifecycle++;if(ownerLifecycle==0)ownerLifecycle=1;
                // A lost or newly acquired table owner must never continue a search
                // that was captured under the previous owner lifecycle.  OnOwnership-
                // transferred normally reaches this path too, but keeping the guard
                // here covers callback ordering/races in ClientSim and VRChat.
                InvalidateSearch();
            }
            if(!authority)
            {
                controllerState=STATE_IDLE;lastError="";RefreshSurfaces();return;
            }
            // A watchdog/readback failure is an explicit terminal state for
            // this search attempt. Do not immediately restart every frame and
            // turn a failed reader into a busy loop; a match reset, profile
            // change, ownership recovery, or an explicit retry clears it.
            if(controllerState==STATE_ERROR)
            {
                RefreshSurfaces();
                return;
            }
            GoDifficultyProfile activeProfile=GetActiveDifficulty();
            bool aiTurn=IsAiTurn();
            if(hintRequested &&
                (game.revision!=hintRootRevision ||
                 ownerLifecycle!=hintRootOwnerLifecycle ||
                 game.sideToMove!=hintRootSide ||
                 game.gameState!=GoGame.STATE_PLAYING || !game.matchStarted || aiTurn))
            {
                hintRequested=false;
                InvalidateSearch();
                if(game.hintMove!=GoGame.NONE)game.ClearPublishedAiHint();
                RefreshSurfaces();
                return;
            }
            if((search.phase==GoMctsSearch.PHASE_WAITING_NEURAL||search.phase==GoMctsSearch.PHASE_EXPANDING||search.phase==GoMctsSearch.PHASE_SELECTING||search.phase==GoMctsSearch.PHASE_BACKING_UP||search.phase==GoMctsSearch.PHASE_RUNNING)&&(game.revision!=search.rootRevision||ownerLifecycle!=search.rootOwnerLifecycle))
            {
                InvalidateSearch();
            }
            // The scheduler may defer GPU/search work, but it must never defer
            // authority, revision, ownership-transfer, or stale-result checks.
            // Xiangqi applies the same distinction in its GPU dispatcher.
            if(schedulerEnabled&&!ShouldDispatchForFrame(Time.frameCount))
            {
                schedulerSkipCount++;
                schedulerLastSkipFrame=Time.frameCount;
                RefreshSurfaces();
                return;
            }
            if(schedulerEnabled)
            {
                schedulerDispatchCount++;
                schedulerLastDispatchFrame=Time.frameCount;
            }
            if(CheckSearchWatchdog())return;
            // Warm the owning table immediately after an AI match starts,
            // even when the first turn belongs to the human. This moves the
            // cold allocation cost out of the first real neural visit while
            // keeping all other tables and non-owners idle.
            if(game.matchStarted&&game.HasAnyAI()&&IsLocalGameOwner()&&
                !resourceWarmupComplete)
            {
                RequestAiResourceWarmup();
                if(resourceWarmupActive)
                {
                    StepResourceWarmup();RefreshSurfaces();return;
                }
            }
            if(game.gameState!=GoGame.STATE_PLAYING||!game.matchStarted||(!aiTurn&&!hintRequested))
            {
                if(search.phase==GoMctsSearch.PHASE_WAITING_NEURAL||search.phase==GoMctsSearch.PHASE_EXPANDING||search.phase==GoMctsSearch.PHASE_SELECTING||search.phase==GoMctsSearch.PHASE_BACKING_UP||search.phase==GoMctsSearch.PHASE_RUNNING)InvalidateSearch();
                controllerState=STATE_IDLE;lastError="";RefreshSurfaces();return;
            }
            float deadline=BeginGrantedFrameBudget();
            PumpSearchUntilDeadline(deadline);
            if(ownershipTelemetryActive&&RemainingMilliseconds(deadline)>0f)
                StepOwnershipTelemetry(deadline);
            EndGrantedFrameBudget();
            RefreshSurfaces();
        }

        public void InvalidateSearch()
        {
            WakeForStateChange();
            bool cancellingPipelineWarmup=resourceWarmupActive&&
                resourceWarmupStage>=4&&resourceWarmupStage<6;
            if(runtime!=null&&runtime.pipelineWarmupActive)
                runtime.CancelPipelineWarmup();
            if(cancellingPipelineWarmup)
            {
                resourceWarmupActive=false;resourceWarmupComplete=false;
                resourceWarmupStage=0;resourceWarmupError="";
                pipelineWarmupReadbackComplete=false;
            }
            if(search!=null&&(search.phase!=GoMctsSearch.PHASE_IDLE&&search.phase!=GoMctsSearch.PHASE_CANCELLED))search.CancelSearch();
            GoGpuNeuralOutputReader invalidatedReader=reader;
            bool abandonedReadback=invalidatedReader!=null&&
                invalidatedReader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING;
            if(abandonedReadback)invalidatedReader.CancelAsyncReadback();
            if(abandonedReadback||IsReaderQuarantined(invalidatedReader))TryRotateReader();
            if(invalidatedReader!=null)invalidatedReader.ReleaseResources();
            if(encoder!=null)encoder.CancelEncode();
            if(runtime!=null)runtime.ReleaseResources();
            CancelSuperkoPreparation();readerPoolWaitStartedAt=0f;
            ClearNeuralTelemetry();
            controllerState=STATE_IDLE;
            FinishPerformance();
        }

        public void ReleaseHiddenTableResources()
        {
            InvalidateSearch();
            if(search!=null)search.ReleaseHeavyResources();
            if(encoder!=null)encoder.ReleaseHeavyResources();
            // A quarantined VRC readback still owns the packed texture until
            // its callback arrives. Managed tree/ladder arrays are safe to
            // release immediately; GPU textures are destroyed only when all
            // three fixed callback channels are quiescent.
            if(runtime!=null&&!HasOutstandingReaderCallback())runtime.ReleaseAllResources();
            resourceWarmupActive=false;resourceWarmupComplete=false;
            resourceWarmupStage=0;resourceWarmupError="";
            pipelineWarmupReadbackComplete=false;
            heavyResourcesReleased=true;
        }

        private bool HasOutstandingReaderCallback()
        {
            InitializeReaderPool();
            return IsReaderOutstanding(readerA)||IsReaderOutstanding(readerB)||
                IsReaderOutstanding(readerC);
        }

        private bool IsReaderOutstanding(GoGpuNeuralOutputReader candidate)
        {
            return candidate!=null&&(candidate.readbackState==
                GoGpuNeuralOutputReader.READBACK_WAITING||
                candidate.readbackState==GoGpuNeuralOutputReader.READBACK_QUARANTINED||
                candidate.quarantinePending);
        }

        private void ClearNeuralTelemetry()
        {
            lastNeuralScoreMean=0f;
            lastNeuralScoreStdev=0f;
            lastNeuralLead=0f;
            lastNeuralOwnershipMean=0f;
            lastNeuralBlackWinProbability=0f;
            lastNeuralWhiteWinProbability=0f;
            lastNeuralNoResultProbability=0f;
            lastNeuralValue=0f;
            lastNeuralOutputRevision=0;
            lastNeuralOwnershipOutputRevision=0;
            ownershipTelemetryActive=false;
            ownershipTelemetryInitialized=false;
            ownershipTelemetryToken=-1;ownershipTelemetryNode=-1;
            ownershipTelemetryRevision=-1;ownershipTelemetrySettingsRevision=-1;
            ownershipTelemetryOwnerLifecycle=-1;ownershipTelemetrySide=GoGame.EMPTY;
            ownershipTelemetryCursor=0;
            ownershipTelemetrySum=0f;
            ClearRootNeuralSnapshot();
        }

        /// <summary>
        /// Matches the Xiangqi family scheduler: when several GPU searches are
        /// active, each controller receives a rotating rank window rather than
        /// a permanently privileged modulo slot. A standalone table bypasses
        /// this gate completely.
        /// </summary>
        public bool ShouldDispatchForFrame(int frameNumber)
        {
            if(!schedulerEnabled)return true;
            int active=Mathf.Clamp(schedulerActiveSearches,1,GoBoardPool.MAX_TABLES);
            int capacity=Mathf.Clamp(schedulerDispatchesPerFrame,1,active);
            if(capacity>=active)return true;
            int rank=Mathf.Clamp(schedulerSlot,0,active-1);
            int phase=frameNumber%active;
            if(phase<0)phase+=active;
            int firstRank=(phase*capacity)%active;
            int distance=rank-firstRank;
            if(distance<0)distance+=active;
            return distance<capacity;
        }

        private bool IsReaderQuarantined(GoGpuNeuralOutputReader candidate)
        {
            return candidate!=null&&candidate.readbackState!=GoGpuNeuralOutputReader.READBACK_WAITING&&
                !candidate.CanBeginAsyncReadback();
        }

        private bool TryRotateReader()
        {
            InitializeReaderPool();
            int start=activeReaderIndex;
            for(int offset=1;offset<=3;offset++)
            {
                int index=(start+offset)%3;
                GoGpuNeuralOutputReader candidate=GetPoolReader(index);
                if(candidate==null||candidate==reader||!candidate.CanBeginAsyncReadback())continue;
                bool recovered=candidate.readbackState==
                    GoGpuNeuralOutputReader.READBACK_RECOVERED;
                reader=candidate;activeReaderIndex=index;readerRotations++;
                if(recovered)recoveredReaderSelections++;
                return true;
            }
            return reader!=null&&reader.CanBeginAsyncReadback();
        }

        private bool EnsureReaderAvailable()
        {
            InitializeReaderPool();
            if(reader==null)
            {
                for(int i=0;i<3;i++)
                {
                    GoGpuNeuralOutputReader candidate=GetPoolReader(i);
                    if(candidate==null||!candidate.CanBeginAsyncReadback())continue;
                    reader=candidate;activeReaderIndex=i;readerRotations++;return true;
                }
                return false;
            }
            if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING||reader.CanBeginAsyncReadback())return true;
            return TryRotateReader();
        }

        private void InitializeReaderPool()
        {
            if(readerA==null)readerA=reader;
            if(readerB==null)readerB=alternateReader;
            if(readerC==null)readerC=tertiaryReader;
            if(reader==null)
            {
                reader=readerA!=null?readerA:(readerB!=null?readerB:readerC);
            }
            if(reader==readerB)activeReaderIndex=1;
            else if(reader==readerC)activeReaderIndex=2;
            else activeReaderIndex=0;
        }

        private GoGpuNeuralOutputReader GetPoolReader(int index)
        {
            if(index==0)return readerA;
            if(index==1)return readerB;
            return readerC;
        }

        private float GetWorkBudgetMilliseconds()
        {
            float budget=schedulerEnabled?schedulerWorkBudgetMs:DEFAULT_WORK_BUDGET_MS;
            return Mathf.Clamp(budget,MIN_WORK_BUDGET_MS,MAX_WORK_BUDGET_MS);
        }

        private float RemainingMilliseconds(float deadline)
        {
            return Mathf.Max(0f,(deadline-Time.realtimeSinceStartup)*1000f);
        }

        private float BeginGrantedFrameBudget()
        {
            float granted=GetWorkBudgetMilliseconds();
            grantedFrameWorkMs=granted;
            // BoardPool has already divided the room budget by the number of
            // admitted dispatches before this controller is ticked. Report
            // that per-dispatch grant, not the unsplit room total.
            float perDispatchGpu=granted;
            if(schedulerEnabled&&boardPool!=null)
                perDispatchGpu=boardPool.localGpuFrameBudgetMs/
                    Mathf.Max(1,schedulerDispatchesPerFrame);
            if(runtime!=null&&runtime.maxGpuSubmitMillisecondsPerFrame>0f)
                perDispatchGpu=Mathf.Min(perDispatchGpu,
                    runtime.maxGpuSubmitMillisecondsPerFrame);
            grantedGpuBudgetMs=Mathf.Clamp(perDispatchGpu,0.25f,6.00f);
            activeFrameDeadline=Time.realtimeSinceStartup+granted*0.001f;
            activeFrameBudget=true;activePumpSafety=0;
            return activeFrameDeadline;
        }

        private void EndGrantedFrameBudget()
        {
            if(!activeFrameBudget)return;
            usedFrameWorkMs=(Time.realtimeSinceStartup-(activeFrameDeadline-
                grantedFrameWorkMs*0.001f))*1000f;
            float overshoot=usedFrameWorkMs-grantedFrameWorkMs;
            if(overshoot>maxFrameWorkOvershootMs)maxFrameWorkOvershootMs=overshoot;
            usedGpuSubmitMs=runtime==null?0f:runtime.lastGraphDispatchMilliseconds;
            activeFrameBudget=false;
            if(usedFrameWorkMs>=grantedFrameWorkMs)deadlineYieldCount++;
        }

        private bool CheckSearchWatchdog()
        {
            if(search==null||
                (search.phase!=GoMctsSearch.PHASE_WAITING_NEURAL&&
                 search.phase!=GoMctsSearch.PHASE_EXPANDING&&
                 search.phase!=GoMctsSearch.PHASE_SELECTING&&
                 search.phase!=GoMctsSearch.PHASE_BACKING_UP&&
                 search.phase!=GoMctsSearch.PHASE_RUNNING))return false;
            if(search.visitsCompleted!=searchLastProgressVisits)
            {
                searchLastProgressVisits=search.visitsCompleted;
                searchLastProgressAt=Time.realtimeSinceStartup;
                return false;
            }
            if(Time.realtimeSinceStartup-searchLastProgressAt<SEARCH_STALL_TIMEOUT_SECONDS)return false;
            searchWatchdogTrips++;
            SetError("AI search made no progress for 30 seconds · retry the search");
            return true;
        }

        public void OnDifficultyChanged()
        {
            WakeForStateChange();
            // Difficulty is configuration state, not position identity. The
            // current SearchSession keeps its captured profile; the new
            // profile is resolved only by the next BeginSearch call.
            if(search==null||search.phase==GoMctsSearch.PHASE_IDLE||
                search.phase==GoMctsSearch.PHASE_CANCELLED||
                search.phase==GoMctsSearch.PHASE_COMPLETE)
                controllerState=STATE_IDLE;
            lastError="";RefreshSurfaces();
        }

        public void OnDifficultyChanged(GoDifficultyProfile changedProfile)
        {
            if(game!=null)
            {
                if(changedProfile==blackDifficulty)game.NotifyAIProfileChanged(GoGame.BLACK);
                else if(changedProfile==whiteDifficulty)game.NotifyAIProfileChanged(GoGame.WHITE);
            }
            OnDifficultyChanged();
        }

        public void OnDifficultyDeserialized()
        {
            WakeForStateChange();
            // Remote configuration updates must not cancel a valid local
            // search. They are visible to the next search after clamping.
            if(ui!=null)ui.NotifyAppliedDifficultyChanged();
            if(search==null||search.phase==GoMctsSearch.PHASE_IDLE||
                search.phase==GoMctsSearch.PHASE_CANCELLED||
                search.phase==GoMctsSearch.PHASE_COMPLETE)
                controllerState=STATE_IDLE;
            lastError="";RefreshSurfaces();
        }

        public void RetrySearch()
        {
            if(game==null||!IsAiTurn()&&!hintRequested)return;
            InvalidateSearch();controllerState=STATE_IDLE;lastError="";RefreshSurfaces();
        }

        public void ToggleHint()
        {
            if(hintRequested)
            {
                hintRequested=false;
                InvalidateSearch();
                if(game!=null)game.ClearPublishedAiHint();
                lastError="";
                RefreshSurfaces();
                return;
            }
            if(game==null)return;
            if(!game.aiHintsEnabled)
            {
                lastError="AI Hint is locked off · enable it before Start Match";
                RefreshSurfaces();
                return;
            }
            if(game.IsAIControlled(game.sideToMove))
            {
                lastError="AI Hint is only available on a human-controlled turn";
                RefreshSurfaces();
                return;
            }
            game.RequestAiHint();
        }

        public void BeginHintSearch()
        {
            WakeForStateChange();
            if(game==null||game.IsAIControlled(game.sideToMove)||
                game.gameState!=GoGame.STATE_PLAYING||!game.matchStarted||
                !game.aiHintsEnabled||!game.aiHintPermissionLocked)
            {
                hintRequested=false;
                return;
            }
            hintRequested=true;
            hintRootRevision=game.revision;
hintRootSettingsRevision=game.settingsRevision;
hintRootOwnerLifecycle=ownerLifecycle;
hintRootSide=game.sideToMove;
            // GoGame.RequestAiHint already invalidates the previous search
// before it increments the hint/position identity. This method is
// the continuation of that command; cancelling again can
// quarantine the same reader twice and needlessly rotate the pool.
controllerState=STATE_IDLE;
            lastError="";
            RefreshSurfaces();
        }

        /// <summary>
        /// Makes the current root estimate playable immediately.  This is an
        /// explicit owner action: it never invents a move and only accepts a
        /// legal root candidate/prior bound to the active search identity.
        /// </summary>
        public void CommitCurrentEstimate()
        {
            if(game==null||search==null)
            {
                lastError="AI estimate unavailable";RefreshSurfaces();return;
            }
            if(!IsAiTurn()||game.gameState!=GoGame.STATE_PLAYING||!game.matchStarted)
            {
                lastError="AI is not the current side";RefreshSurfaces();return;
            }
            if(!TakeOwnership())
            {
                lastError="AI move denied · authoritative Go owner unavailable";
                RefreshSurfaces();return;
            }
            if(search.rootRevision!=game.revision||
                search.rootOwnerLifecycle!=ownerLifecycle)
            {
                lastError="AI estimate is stale · continuing with a fresh search";
                InvalidateSearch();RefreshSurfaces();return;
            }
            int move=search.bestMove;
            if(move==GoGame.NONE)move=search.GetRootCandidateMove(0);
            if(move==GoGame.NONE)move=search.GetRootPriorMove();
            if(move==GoGame.NONE)
            {
                lastError="AI estimate is not ready · waiting for the root evaluation";
                RefreshSurfaces();return;
            }
            if(move!=GoGame.PASS&&!game.IsRulesLegalMove(move))
            {
                lastError="AI estimate became illegal · continuing with a fresh search";
                InvalidateSearch();RefreshSurfaces();return;
            }
            if(telemetry!=null)telemetry.CaptureFinalAnalysisDeferred(search,this,"AI immediate estimate");
            int before=game.revision;
            if(move==GoGame.PASS)game.PassFromAI();
            else game.TryPlayFromAI(move);
            if(game.revision==before)
            {
                if(telemetry!=null)telemetry.PublishDeferredFinalAnalysis();
                lastError="AI estimate could not be committed: "+search.lastError;
                RefreshSurfaces();return;
            }
            aiMoves++;
            PublishDeferredTelemetryAfterMove();
            runtime.ReleaseResources();
            search.CancelSearch();
            FinishPerformance();
            controllerState=STATE_IDLE;lastError="";
            RefreshSurfaces();
        }

        public bool HasCurrentEstimate()
        {
            if(game==null||search==null||!game.matchStarted||
                game.gameState!=GoGame.STATE_PLAYING||!IsAiTurn())return false;
            if(search.rootRevision!=game.revision||
                search.rootOwnerLifecycle!=ownerLifecycle)return false;
            if(search.bestMove!=GoGame.NONE)return true;
            return search.GetRootCandidateMove(0)!=GoGame.NONE||
                search.GetRootPriorMove()!=GoGame.NONE;
        }

        public void SetMode(int requested)
        {
            if(requested<MODE_PVP||requested>MODE_AIVAI)requested=MODE_PVAI;
            if(game!=null){game.SetMatchMode(requested);SyncControllerMirrorFromGame();return;}
            if(mode==requested)return;if(!TakeOwnership()){lastError="AI mode change denied · authoritative owner unavailable";RefreshSurfaces();return;}mode=requested;previousMode=mode;InvalidateSearch();RequestSerialization();RefreshSurfaces();
        }

        public void SetAiColor(int color)
        {
            if(color!=GoGame.BLACK&&color!=GoGame.WHITE)return;
            if(game!=null){game.SetAiSide(color);SyncControllerMirrorFromGame();return;}
            if(aiColor==color)return;if(!TakeOwnership()){lastError="AI side change denied · authoritative owner unavailable";RefreshSurfaces();return;}aiColor=color;previousAiColor=aiColor;InvalidateSearch();RequestSerialization();RefreshSurfaces();
        }

        public string GetStatusText()
        {
            if(controllerState==STATE_ERROR)return "AI error: "+lastError;
            if(game==null)return "AI unavailable";
            if(resourceWarmupActive)return "AI preparing GPU";
            if(hintRequested)
            {
                if(search==null)return "AI Hint · unavailable";
                if(search.phase==GoMctsSearch.PHASE_WAITING_NEURAL)return "AI Hint · GPU evaluation";
                if(search.phase==GoMctsSearch.PHASE_EXPANDING)return "AI Hint · legal move expansion";
                if(search.phase==GoMctsSearch.PHASE_SELECTING)return "AI Hint · selecting line";
                if(search.phase==GoMctsSearch.PHASE_BACKING_UP)return "AI Hint · backing up value";
                if(search.phase==GoMctsSearch.PHASE_RUNNING)return "AI Hint · visit "+search.visitsCompleted+"/"+search.targetVisits;
                if(search.phase==GoMctsSearch.PHASE_COMPLETE)return "AI Hint · finalizing";
                return "AI Hint · searching";
            }
            if(game!=null&&!game.matchStarted)return game.GetMatchModeName(true)+" · Ready to start";
            if(game!=null&&game.GetAIControlCount()==0)return "PvP · AI off";
            if(!IsAiTurn())return "Waiting for player";
            if(search==null)return "AI unavailable";
            if(search.phase==GoMctsSearch.PHASE_WAITING_NEURAL)
            {
                return reader!=null&&reader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING?"Thinking · GPU readback":"Thinking · GPU evaluation";
            }
            if(search.phase==GoMctsSearch.PHASE_EXPANDING)return "Thinking · expanding legal moves";
            if(search.phase==GoMctsSearch.PHASE_SELECTING)return "Thinking · selecting line";
            if(search.phase==GoMctsSearch.PHASE_BACKING_UP)return "Thinking · backing up value";
            if(search.phase==GoMctsSearch.PHASE_RUNNING)return "Thinking · visit "+search.visitsCompleted+"/"+search.targetVisits;
            if(search.phase==GoMctsSearch.PHASE_COMPLETE)return "Choosing move · "+search.visitsCompleted+" visits";
            return "AI ready";
        }

        public string GetStatusTextLocalized(bool english)
        {
            string value=GetStatusText();
            if(english)return value;
            value=value.Replace("AI error: ","AI 错误：");
            value=value.Replace("AI preparing GPU","AI 正在准备 GPU");
            value=value.Replace("AI Hint · GPU evaluation","AI 提示 · GPU 计算");
            value=value.Replace("AI Hint · legal move expansion","AI 提示 · 展开合法着法");
            value=value.Replace("AI Hint · selecting line","AI 提示 · 选择变化");
            value=value.Replace("AI Hint · backing up value","AI 提示 · 回传局面价值");
            value=value.Replace("AI Hint · visit ","AI 提示 · 搜索 ");
            value=value.Replace("AI Hint · finalizing","AI 提示 · 整理结果");
            value=value.Replace("AI Hint · searching","AI 提示 · 搜索中");
            value=value.Replace("PvP · AI off","PvP · AI 未启用");
            value=value.Replace("Player vs Player · Ready to start","玩家 vs 玩家 · 等待开始");
            value=value.Replace("Player vs AI · Ready to start","玩家 vs AI · 等待开始");
            value=value.Replace("AI vs AI · Ready to start","AI vs AI · 等待开始");
            value=value.Replace("Waiting for player","等待玩家");
            value=value.Replace("Thinking · GPU readback","思考中 · GPU 回读");
            value=value.Replace("Thinking · GPU evaluation","思考中 · GPU 计算");
            value=value.Replace("Thinking · expanding legal moves","思考中 · 展开合法着法");
            value=value.Replace("Thinking · selecting line","思考中 · 选择变化");
            value=value.Replace("Thinking · backing up value","思考中 · 回传局面价值");
            value=value.Replace("Thinking · visit ","思考中 · 搜索 ");
            value=value.Replace("Choosing move · ","选择着法 · ");
            value=value.Replace(" visits"," 次访问");
            value=value.Replace("AI ready","AI 就绪");
            value=value.Replace("AI unavailable","AI 不可用");
            return value;
        }

        public string GetHintStatusText(bool english)
        {
            if(game==null)return english?"AI Hint unavailable":"AI 提示不可用";
            if(!game.matchStarted&&!game.aiHintPermissionLocked)
                return game.aiHintsEnabled
                    ?(english?"AI Hint · enabled · press Start Match to lock":"AI 提示 · 已开启 · 点击开始后锁定")
                    :(english?"AI Hint · enable before Start Match":"AI 提示 · 请在开局前开启");
            if(game.aiHintPermissionLocked&&!game.aiHintsEnabled)
                return english?"AI Hint · locked for this match":"AI 提示 · 本局已锁定关闭";
            if(hintRequested)
            {
                if(search!=null&&search.phase==GoMctsSearch.PHASE_EXPANDING)
                    return english?"AI Hint · expanding legal moves":"AI 提示 · 展开合法着法";
                if(search!=null&&search.phase==GoMctsSearch.PHASE_SELECTING)
                    return english?"AI Hint · selecting line":"AI 提示 · 选择变化";
                if(search!=null&&search.phase==GoMctsSearch.PHASE_BACKING_UP)
                    return english?"AI Hint · backing up value":"AI 提示 · 回传局面价值";
                if(search!=null&&search.phase==GoMctsSearch.PHASE_RUNNING)
                    return english?"AI Hint · visit "+search.visitsCompleted+"/"+search.targetVisits:"AI 提示 · 搜索 "+search.visitsCompleted+"/"+search.targetVisits;
                return english?"AI Hint · evaluating without auto-play":"AI 提示 · 计算中，不会自动落子";
            }
            if(game.hintMove==GoGame.PASS)return english?"AI Hint · recommends pass":"AI 提示 · 建议停一手";
            if(game.hintMove>=0)return english?"AI Hint · recommends "+game.GetCoordinateLabel(game.hintMove):"AI 提示 · 建议 "+game.GetCoordinateLabel(game.hintMove);
            return english?"AI Hint · ready for a human turn":"AI 提示 · 等待真人回合";
        }

        private bool IsAiTurn()
        {
            return game!=null&&game.IsAIControlled(game.sideToMove);
        }

        public GoDifficultyProfile GetActiveDifficulty()
        {
            if(game!=null)
            {
                if(game.sideToMove==GoGame.BLACK&&blackDifficulty!=null)return blackDifficulty;
                if(game.sideToMove==GoGame.WHITE&&whiteDifficulty!=null)return whiteDifficulty;
            }
            return difficulty;
        }

        private void BeginSearch()
        {
            GoDifficultyProfile active=GetActiveDifficulty();
            if(active==null){SetError("active Go difficulty profile is missing");return;}
            if(game==null||!game.HasVerifiedPositionHistory())
            {
                SetError("Go move history could not be verified; search is blocked");
                return;
            }
            heavyResourcesReleased=false;
            float setupStartedAt=Time.realtimeSinceStartup;
            int visits=Mathf.Min(active.maxVisits,active.maxNNQueries);
            if(visits<1)visits=1;
            search.explorationConstant=active.cpuct;
            activeSearchTransitionsPerFrame=Mathf.Clamp(active.maxTransitionsPerFrame,1,8);
            activeSearchResignThreshold=active.resignThreshold;
            int seed=game.revision*1103515245^game.settingsRevision*1664525^active.settingsRevision*1013904223^ownerLifecycle*374761393^game.sideToMove*668265263;
            search.ConfigureRootSelection(active.moveTemperature,active.policyTopK,seed);
            if(!search.BeginSearch(game,visits,ownerLifecycle,active.settingsRevision)){SetError(search.lastError);return;}
            lastSearchTargetVisits=visits;lastSearchCompletedVisits=0;lastSearchFrames=0;
            lastSearchMilliseconds=0f;lastSearchSetupMilliseconds=0f;lastSearchStepMilliseconds=0f;
            lastSuperkoMilliseconds=0f;lastFeatureMilliseconds=0f;
            lastFeatureActiveCpuMilliseconds=0f;lastLadderActiveCpuMilliseconds=0f;
            lastGpuInputSetupMilliseconds=0f;lastGpuUploadMilliseconds=0f;
            lastGpuInputPackingMilliseconds=0f;lastGpuSpatialApplyMilliseconds=0f;
            lastGpuGlobalApplyMilliseconds=0f;lastGpuInputBlitMilliseconds=0f;
            lastGpuDispatchMilliseconds=0f;lastGpuEvaluationMilliseconds=0f;
            lastReadbackMilliseconds=0f;lastNeuralSubmitMilliseconds=0f;lastCommitMilliseconds=0f;
            lastFeatureSteps=0;lastLadderStepCalls=0;lastLadderSearchTransitions=0;lastLadderNodes=0;
            lastGpuPasses=0;lastReadbackRequests=0;lastReadbackStages=0;
            lastGpuGraphFrames=0;gpuGraphPassesThisFrame=0;
            maxGpuGraphPassesOneFrame=0;gpuGraphVisitedStageMask=0;
            gpuGraphStage=GoGpuNeuralRuntime.NN_IDLE;gpuGraphStageProgress=0;
            maxGpuSubmitMsOneFrame=0f;smoothedGpuSubmitMilliseconds=0f;
            lastSearchStepCalls=0;lastLegalMoveChecks=0;lastSimulationPlayCalls=0;
            lastSuperkoScanLocations=0;lastSuperkoScanFrames=0;lastHistoryEntriesExamined=0;
            lastHistoryQueryCount=0;lastHistoryBucketHitCount=0;
            lastHistoryBucketMissCount=0;lastHistoryFullScanCount=0;
            lastHistoryMaxEntriesExamined=0;
            lastScoreUtilityMilliseconds=0f;
            lastMctsSelectionMilliseconds=0f;lastMctsExpansionMilliseconds=0f;
            lastMctsBackupMilliseconds=0f;lastFeatureSuperkoMilliseconds=0f;
            lastFeatureClearMilliseconds=0f;lastFeatureBaseMilliseconds=0f;
            lastLadderMilliseconds=0f;lastFeatureAreaMilliseconds=0f;
            superkoProbePoints=0;superkoProbeFrames=0;superkoProbeMilliseconds=0f;
            maxSuperkoPointsOneFrame=0;maxSuperkoFrameMilliseconds=0f;
            currentSuperkoProbeIndex=0;completedSuperkoProbeMasks=0;
            cancelledSuperkoProbes=0;lastCancelledSuperkoProbeIndex=0;
            superkoProbeActive=false;
            ownershipTelemetryPoints=0;ownershipTelemetryFrames=0;
            maxOwnershipTelemetryPointsOneFrame=0;
            maxOwnershipTelemetryFrameMilliseconds=0f;
            ownershipTelemetryActive=false;ownershipTelemetryInitialized=false;
            ownershipTelemetryToken=-1;ownershipTelemetryNode=-1;
            ownershipTelemetryRevision=-1;ownershipTelemetrySettingsRevision=-1;
            ownershipTelemetryOwnerLifecycle=-1;ownershipTelemetrySide=GoGame.EMPTY;
            ownershipTelemetryCursor=0;
            ownershipTelemetrySum=0f;
            lastNeuralOwnershipOutputRevision=0;
            grantedFrameWorkMs=0f;usedFrameWorkMs=0f;maxFrameWorkOvershootMs=0f;
            grantedGpuBudgetMs=0f;usedGpuSubmitMs=0f;
            sameFramePhaseTransitions=0;avoidableYieldCount=0;deadlineYieldCount=0;
            asyncReadbackWaitFrames=0;telemetryCommitsDuringSearch=0;
            telemetrySerializationsDuringSearch=0;searchFrameCount=0;
            visit0CompletionFrame=-1;visit1CompletionFrame=-1;
            visit2CompletionFrame=-1;visit3CompletionFrame=-1;
            lastTickMilliseconds=0f;maxTickMilliseconds=0f;tickSamples=0;
            lastRootCopyMilliseconds=0f;lastNeuralExpansionMilliseconds=0f;lastBackupMilliseconds=0f;
            maxMctsStepMilliseconds=0f;
            superkoScanActive=false;superkoScanCursor=0;readerPoolWaitStartedAt=0f;
            accountedEncodeGeneration=-1;
            searchLastProgressAt=Time.realtimeSinceStartup;searchLastProgressVisits=0;
            ClearRootNeuralSnapshot();
            lastSearchSetupMilliseconds=(Time.realtimeSinceStartup-setupStartedAt)*1000f;
            performanceSearchStartedAt=setupStartedAt;
            performanceSearchStartedFrame=Time.frameCount;
            performanceSearchActive=true;
            controllerState=STATE_THINKING;lastError="";
        }

        private void PumpSearchUntilDeadline(float deadline)
        {
            int safety=0;
            while(safety++<128&&RemainingMilliseconds(deadline)>0f)
            {
                activePumpSafety=safety;
                int phaseBefore=search.phase;
                int visitsBefore=search.visitsCompleted;
                bool progressed=true;
                if(phaseBefore==GoMctsSearch.PHASE_IDLE||
                    phaseBefore==GoMctsSearch.PHASE_CANCELLED)
                {
                    BeginSearch();
                }
                else if(phaseBefore==GoMctsSearch.PHASE_WAITING_NEURAL)
                {
                    progressed=EvaluatePending(deadline);
                    if(reader!=null&&reader.readbackState==
                        GoGpuNeuralOutputReader.READBACK_WAITING)
                    {
                        if(ownershipTelemetryActive&&RemainingMilliseconds(deadline)>0f)
                            StepOwnershipTelemetry(deadline);
                        asyncReadbackWaitFrames++;
                        return;
                    }
                }
                else if(phaseBefore==GoMctsSearch.PHASE_EXPANDING)
                {
                    progressed=StepExpansion(deadline);
                }
                else if(phaseBefore==GoMctsSearch.PHASE_SELECTING||
                    phaseBefore==GoMctsSearch.PHASE_BACKING_UP||
                    phaseBefore==GoMctsSearch.PHASE_RUNNING)
                {
                    progressed=StepSearch(deadline);
                }
                else if(phaseBefore==GoMctsSearch.PHASE_COMPLETE)
                {
                    CommitSearchMove();
                    return;
                }
                else
                {
                    SetError("search entered an unknown phase");
                    return;
                }
                if(controllerState==STATE_ERROR)return;
                if(search.phase!=phaseBefore)sameFramePhaseTransitions++;
                if(search.visitsCompleted>visitsBefore)
                {
                    int completed=search.visitsCompleted;
                    int relativeFrame=Time.frameCount-performanceSearchStartedFrame;
                    if(completed==1)visit0CompletionFrame=relativeFrame;
                    else if(completed==2)visit1CompletionFrame=relativeFrame;
                    else if(completed==3)visit2CompletionFrame=relativeFrame;
                    else if(completed==4)visit3CompletionFrame=relativeFrame;
                }
                if(search.phase==GoMctsSearch.PHASE_COMPLETE)
                {
                    CommitSearchMove();
                    return;
                }
                if(!progressed)
                {
                    if(RemainingMilliseconds(deadline)<=0f)deadlineYieldCount++;
                    else avoidableYieldCount++;
                    return;
                }
                if(search.phase==phaseBefore&&search.visitsCompleted==visitsBefore&&
                    RemainingMilliseconds(deadline)<=0f)
                {
                    deadlineYieldCount++;
                    return;
                }
            }
            if(safety>=128)avoidableYieldCount++;
            else if(RemainingMilliseconds(deadline)<=0f)deadlineYieldCount++;
        }

        private bool EvaluatePending(float deadline)
        {
            if(!EnsureReaderAvailable())
            {
                if(readerPoolWaitStartedAt<=0f)
                {
                    readerPoolWaitStartedAt=Time.realtimeSinceStartup;
                    readerPoolExhaustions++;
                }
                if(Time.realtimeSinceStartup-readerPoolWaitStartedAt>=READER_POOL_WAIT_TIMEOUT_SECONDS)
                {
                    SetError("GPU reader pool exhausted · retry the Go search");
                    return false;
                }
                controllerState=STATE_THINKING;
                lastError="";
                return false;
            }
            readerPoolWaitStartedAt=0f;
            if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING){controllerState=STATE_THINKING;return false;}
            if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_ERROR){SetError(reader.lastError);return false;}
            if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_COMPLETE&&ReaderMatchesPending())
            {
                if(!OwnershipTelemetryMatchesPending())
                {
                    lastReadbackMilliseconds+=reader.lastAsyncReadbackMilliseconds;
                    lastReadbackRequests+=reader.lastAsyncReadbackRequests;
                    lastReadbackStages=reader.completedStages;
                }
                // Ownership is presentation-only. Capture the exact root
                // snapshot and initialize its background conversion, but do
                // not hold policy/value/score submission behind 361 tanh
                // conversions. MCTS can expand this result immediately.
                CaptureNeuralTelemetry();
                float submitStartedAt=Time.realtimeSinceStartup;
                if(!reader.TryBeginSubmitToSearch(search)){SetError(reader.lastError);return false;}
                lastNeuralSubmitMilliseconds+=(Time.realtimeSinceStartup-submitStartedAt)*1000f;
                // Expansion is now a cooperative phase. Keep the completed
                // reader alive until its legal-edge normalization has finished
                // so the controller can release all leaf resources together.
                lastError="";controllerState=STATE_THINKING;return true;
            }
            if(reader.readbackState==GoGpuNeuralOutputReader.READBACK_COMPLETE)
                reader.ReleaseResources();

            // Ladder/area feature work remains cooperative and exact. It now
            // consumes the controller's admitted deadline instead of one fixed
            // board-scan unit per rendered frame. StepEncodeUntil is resumable,
            // so adjacent CPU-only encoder slices drain until this same
            // deadline; feature order and search semantics are unchanged.
            float remainingBeforeEncode=RemainingMilliseconds(deadline);
            if(remainingBeforeEncode<=0f)return false;
            int encodeBudget=Mathf.Clamp(Mathf.RoundToInt(remainingBeforeEncode*128f),32,256);
            if(encoder.encodeState==GoFeatureEncoder.ENCODE_RUNNING)
            {
                float encodeStartedAt=Time.realtimeSinceStartup;
                int encodeCalls=0;
                int encodePhase=encoder.encodeState;
                while(encodePhase==GoFeatureEncoder.ENCODE_RUNNING&&encodeCalls<32)
                {
                    float remaining=RemainingMilliseconds(deadline);
                    if(encodeCalls>0&&remaining<=0f)break;
                    int sliceBudget=encodeCalls==0?encodeBudget:
                        Mathf.Clamp(Mathf.RoundToInt(remaining*128f),1,256);
                    encodePhase=encoder.StepEncodeUntil(sliceBudget,deadline);
                    encodeCalls++;
                    if(encodePhase!=GoFeatureEncoder.ENCODE_RUNNING||
                        RemainingMilliseconds(deadline)<=0f)break;
                }
                if(encodePhase==GoFeatureEncoder.ENCODE_RUNNING)
                {
                    controllerState=STATE_THINKING;return false;
                }
                if(encoder.encodeState==GoFeatureEncoder.ENCODE_ERROR)
                {
                    SetError("feature encoding failed: "+encoder.lastError);return false;
                }
            }
            if(encoder.encodeState==GoFeatureEncoder.ENCODE_COMPLETE)
            {
                if(accountedEncodeGeneration!=encoder.encodeGeneration)
                {
                    accountedEncodeGeneration=encoder.encodeGeneration;
                    lastFeatureMilliseconds+=encoder.lastEncodeMilliseconds;
                    lastFeatureActiveCpuMilliseconds+=encoder.featureActiveCpuMilliseconds;
                    lastLadderActiveCpuMilliseconds+=encoder.ladderActiveCpuMilliseconds;
                    lastFeatureSuperkoMilliseconds+=encoder.lastScanSuperkoMilliseconds;
                    lastFeatureClearMilliseconds+=encoder.lastClearSpatialMilliseconds;
                    lastFeatureBaseMilliseconds+=encoder.lastBaseMilliseconds;
                    lastLadderMilliseconds+=encoder.lastLadderMilliseconds;
                    lastFeatureAreaMilliseconds+=encoder.lastAreaMilliseconds;
                    lastFeatureSteps+=encoder.lastEncodeSteps;
                    lastLadderStepCalls+=encoder.encodeLadderTransitions;
                    lastLadderSearchTransitions+=encoder.lastLadderSearchTransitions;
                    lastLadderNodes+=encoder.lastLadderNodes;
                }
                if(runtime.gpuGraphStage==GoGpuNeuralRuntime.NN_IDLE||
                    runtime.gpuGraphStage==GoGpuNeuralRuntime.NN_CANCELLED)
                {
                    if(!runtime.BeginEvaluateEncoded(encoder.spatialOutput,encoder.globalOutput,
                        search.pendingNode==0,tableIdentity,search.searchToken,search.pendingNode,
                        search.rootRevision,search.rootGameSettingsRevision,
                        search.rootSettingsRevision,search.rootOwnerLifecycle,
                        search.pendingPlayer,search.pendingHash0,search.pendingHash1,
                        search.pendingHash2,search.pendingHash3))
                    {
                        SetError(runtime.lastError);return false;
                    }
                    gpuGraphStage=runtime.gpuGraphStage;
                    gpuGraphStageProgress=runtime.gpuGraphStageProgress;
                    lastError="";controllerState=STATE_THINKING;
                }
                if(runtime.gpuGraphStage!=GoGpuNeuralRuntime.NN_READY_FOR_READBACK)
                {
                    if(!runtime.MatchesGraphIdentity(tableIdentity,search.searchToken,
                        search.pendingNode,search.rootRevision,
                        search.rootGameSettingsRevision,search.rootSettingsRevision,
                        search.rootOwnerLifecycle,search.pendingPlayer,
                        search.pendingHash0,search.pendingHash1,search.pendingHash2,
                        search.pendingHash3))
                    {
                        runtime.ReleaseResources();
                        SetError("stale GPU graph identity was rejected");return false;
                    }
                    float graphBudget=RemainingMilliseconds(deadline);
                    if(graphBudget<=0f)return false;
                    int graphState=runtime.StepEvaluation(graphBudget);
                    gpuGraphPassesThisFrame=runtime.gpuGraphPassesThisFrame;
                    gpuGraphStage=runtime.gpuGraphStage;
                    gpuGraphStageProgress=runtime.gpuGraphStageProgress;
                    lastGpuGraphFrames=runtime.gpuGraphFrames;
                    maxGpuSubmitMsOneFrame=runtime.maxGpuSubmitMsOneFrame;
                    smoothedGpuSubmitMilliseconds=runtime.smoothedGpuSubmitMilliseconds;
                    maxGpuGraphPassesOneFrame=runtime.maxGpuGraphPassesOneFrame;
                    gpuGraphVisitedStageMask=runtime.gpuGraphVisitedStageMask;
                    lastGpuPasses=runtime.executedPasses;
                    if(graphState==GoGpuNeuralRuntime.NN_ERROR)
                    {SetError(runtime.lastError);return false;}
                    if(graphState!=GoGpuNeuralRuntime.NN_READY_FOR_READBACK)
                    {lastError="";controllerState=STATE_THINKING;return false;}
                }
                lastGpuInputSetupMilliseconds+=runtime.lastInputSetupMilliseconds;
                lastGpuUploadMilliseconds+=runtime.lastInputUploadMilliseconds;
                lastGpuInputPackingMilliseconds+=runtime.lastInputPackingMilliseconds;
                lastGpuSpatialApplyMilliseconds+=runtime.lastSpatialApplyMilliseconds;
                lastGpuGlobalApplyMilliseconds+=runtime.lastGlobalApplyMilliseconds;
                lastGpuInputBlitMilliseconds+=runtime.lastInputBlitMilliseconds;
                lastGpuDispatchMilliseconds+=runtime.lastGraphDispatchMilliseconds;
                lastGpuEvaluationMilliseconds+=runtime.lastEvaluationMilliseconds;
                lastGpuPasses=runtime.executedPasses;
                lastGpuGraphFrames=runtime.gpuGraphFrames;
                maxGpuSubmitMsOneFrame=runtime.maxGpuSubmitMsOneFrame;
                smoothedGpuSubmitMilliseconds=runtime.smoothedGpuSubmitMilliseconds;
                maxGpuGraphPassesOneFrame=runtime.maxGpuGraphPassesOneFrame;
                gpuGraphVisitedStageMask=runtime.gpuGraphVisitedStageMask;
                reader.runtime=runtime;
                if(!reader.BeginAsyncReadback(runtime,search.searchToken,search.pendingNode,
                    search.rootRevision,search.rootSettingsRevision,search.rootOwnerLifecycle,
                    search.pendingHash0,search.pendingHash1,search.pendingHash2,search.pendingHash3,
                    search.pendingNode==0))
                {
                    SetError(reader.lastError);return false;
                }
                lastError="";controllerState=STATE_THINKING;return false;
            }
            if(!reader.CanBeginAsyncReadback())
            {
                if(!TryRotateReader())
                {
                    controllerState=STATE_THINKING;
                    lastError="";
                }
                return false;
            }
            runtime.ReleaseResources();
            if(!StepSuperkoPreparation(deadline))
            {
                controllerState=STATE_THINKING;
                lastError="";
                return false;
            }
            if(!encoder.BeginEncodeState(search.simulationState.board,
                search.simulationState.previousBoard1,search.simulationState.previousBoard2,
                search.simulationState.recentMoveLoc,search.simulationState.recentMovePla,
                search.simulationState.sideToMove,search.simulationState.koLoc,
                search.simulationState.komiTimes2,search.simulationState.positionalSuperko,
                search.simulationState.multiStoneSuicideLegal,search.simulationState.areaScoring,
                search.simulationState.consecutivePasses,search.simulationState.gameState,
                pendingSuperkoMask,encoder.spatialOutput,encoder.globalOutput))
            {
                SetError("feature encoder could not start: "+encoder.lastError);return false;
            }
            lastError="";controllerState=STATE_THINKING;
            return true;
        }

        private bool StepSuperkoPreparation(float deadline)
        {
            bool samePending=superkoScanActive&&superkoScanToken==search.searchToken&&
                superkoScanNode==search.pendingNode&&superkoScanRevision==search.rootRevision&&
                superkoScanGameSettingsRevision==search.rootGameSettingsRevision&&
                superkoScanSettingsRevision==search.rootSettingsRevision&&
                superkoScanOwnerLifecycle==search.rootOwnerLifecycle&&
                superkoScanHash0==search.pendingHash0&&superkoScanHash1==search.pendingHash1&&
                superkoScanHash2==search.pendingHash2&&superkoScanHash3==search.pendingHash3;
            if(!samePending)
            {
                superkoScanActive=true;
                superkoScanCursor=0;
                superkoScanToken=search.searchToken;
                superkoScanNode=search.pendingNode;
                superkoScanRevision=search.rootRevision;
                superkoScanGameSettingsRevision=search.rootGameSettingsRevision;
                superkoScanSettingsRevision=search.rootSettingsRevision;
                superkoScanOwnerLifecycle=search.rootOwnerLifecycle;
                superkoScanHash0=search.pendingHash0;
                superkoScanHash1=search.pendingHash1;
                superkoScanHash2=search.pendingHash2;
                superkoScanHash3=search.pendingHash3;
                currentSuperkoProbeIndex=0;
                superkoProbeActive=true;
            }

            // Re-check the complete async identity at the stage boundary. A
            // position/owner change is handled before any more mask
            // entries can be written, even if this method is called directly
            // by a verifier rather than through Tick's outer stale guard.
            if(!IsSuperkoPreparationCurrent())
            {
                CancelSuperkoPreparation();
                return false;
            }

            float startedAt=Time.realtimeSinceStartup;
            float remainingBudget=RemainingMilliseconds(deadline);
            if(remainingBudget<=0f)return false;
            int locationBudget=Mathf.Clamp(Mathf.RoundToInt(remainingBudget*64f),
                MIN_SUPERKO_LOCATIONS_PER_FRAME,MAX_SUPERKO_LOCATIONS_PER_FRAME);
            int completed=0;
            while(superkoScanCursor<GoGame.AREA&&completed<locationBudget)
            {
                int moveMask=search.simulationState.GetMoveMask(superkoScanCursor);
                pendingSuperkoMask[superkoScanCursor]=(moveMask&2)!=0;
                superkoScanCursor++;completed++;
                if(completed>0&&Time.realtimeSinceStartup>=deadline)break;
            }
            float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
            lastSuperkoScanLocations+=completed;
            lastSuperkoScanFrames++;
            lastSuperkoMilliseconds+=elapsed;
            superkoProbePoints+=completed;
            superkoProbeFrames++;
            superkoProbeMilliseconds+=elapsed;
            if(completed>maxSuperkoPointsOneFrame)maxSuperkoPointsOneFrame=completed;
            if(elapsed>maxSuperkoFrameMilliseconds)maxSuperkoFrameMilliseconds=elapsed;
            currentSuperkoProbeIndex=superkoScanCursor;
            if(superkoScanCursor<GoGame.AREA)return false;
            superkoScanActive=false;
            superkoProbeActive=false;
            completedSuperkoProbeMasks++;
            return true;
        }

        private bool IsSuperkoPreparationCurrent()
        {
            if(game==null||search==null)return false;
            return superkoScanActive&&search.phase==GoMctsSearch.PHASE_WAITING_NEURAL&&
                superkoScanToken==search.searchToken&&superkoScanNode==search.pendingNode&&
                superkoScanRevision==search.rootRevision&&
                superkoScanOwnerLifecycle==search.rootOwnerLifecycle&&
                superkoScanHash0==search.pendingHash0&&superkoScanHash1==search.pendingHash1&&
                superkoScanHash2==search.pendingHash2&&superkoScanHash3==search.pendingHash3&&
                game.revision==search.rootRevision&&
                ownerLifecycle==search.rootOwnerLifecycle;
        }

        private void CancelSuperkoPreparation()
        {
            if(superkoScanActive)
            {
                cancelledSuperkoProbes++;
                lastCancelledSuperkoProbeIndex=superkoScanCursor;
            }
            superkoScanActive=false;
            superkoProbeActive=false;
            superkoScanCursor=0;
            currentSuperkoProbeIndex=0;
        }

        private bool StepExpansion(float deadline)
        {
            float remainingBudget=RemainingMilliseconds(deadline);
            if(remainingBudget<=0f)return false;
            int expansionBudget=Mathf.Clamp(Mathf.RoundToInt(remainingBudget*128f),1,256);
            float startedAt=Time.realtimeSinceStartup;
            int phase=search.phase;
            int calls=0;
            // StepPendingExpansion is internally resumable. Keep advancing
            // cheap CPU stages (legal mask, edge normalization, score utility)
            // while this Tick still owns its time slice instead of turning
            // every small chunk into a mandatory frame barrier. A bounded call
            // cap protects the VM if its clock has insufficient resolution.
            while(phase==GoMctsSearch.PHASE_EXPANDING&&calls<32)
            {
                float remaining=(deadline-Time.realtimeSinceStartup)*1000f;
                if(calls>0&&remaining<=0f)break;
                int sliceBudget=calls==0?expansionBudget:
                    Mathf.Clamp(Mathf.RoundToInt(remaining*128f),1,256);
                phase=search.StepPendingExpansionUntil(sliceBudget,deadline);
                calls++;
                if(phase!=GoMctsSearch.PHASE_EXPANDING||Time.realtimeSinceStartup>=deadline)break;
            }
            lastSearchStepMilliseconds+=(Time.realtimeSinceStartup-startedAt)*1000f;
            float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
            lastMctsExpansionMilliseconds+=elapsed;
            if(elapsed>maxMctsStepMilliseconds)maxMctsStepMilliseconds=elapsed;
            if(phase==GoMctsSearch.PHASE_CANCELLED)
            {
                SetError(search.lastError);
                return false;
            }
            if(phase!=GoMctsSearch.PHASE_EXPANDING)
            {
                // A completed encoder/readback pair belongs only to this
                // leaf. Release it after expansion, never before.
                encoder.CancelEncode();
                reader.ReleaseResources();
                runtime.ReleaseResources();
                lastError="";
            }
            controllerState=STATE_THINKING;
            return phase!=GoMctsSearch.PHASE_EXPANDING||
                RemainingMilliseconds(deadline)>0f;
        }

        private bool CaptureNeuralTelemetry()
        {
            if(reader==null||search==null||reader.score==null||reader.score.Length<4||
                reader.value==null||reader.value.Length<3||reader.ownership==null||
                reader.ownership.Length<GoGame.AREA)
                return true;

            // Presentation telemetry is root-only.  Leaf NN results are
            // consumed directly by GoMctsSearch and must never replace the
            // score/winrate/ownership estimate for the analyzed root.
            if(search.pendingNode!=0)
                return true;

            bool rootTelemetryInFlight=ownershipTelemetryActive&&
                ownershipTelemetryInitialized&&ownershipTelemetryNode==0;
            bool sameCapture=ownershipTelemetryInitialized&&
                ownershipTelemetryToken==search.searchToken&&
                ownershipTelemetryRevision==search.rootRevision&&
                ownershipTelemetrySettingsRevision==search.rootSettingsRevision&&
                ownershipTelemetryOwnerLifecycle==search.rootOwnerLifecycle&&
                ownershipTelemetryNode==0&&search.pendingNode==0;
            if(!sameCapture&&!rootTelemetryInFlight)
            {
                ownershipTelemetryInitialized=true;
                ownershipTelemetryToken=search.searchToken;
                ownershipTelemetryNode=search.pendingNode;
                ownershipTelemetryRevision=search.rootRevision;
                ownershipTelemetrySettingsRevision=search.rootSettingsRevision;
                ownershipTelemetryOwnerLifecycle=search.rootOwnerLifecycle;
                ownershipTelemetrySide=search.pendingPlayer;
                ownershipTelemetryHash0=search.pendingHash0;
                ownershipTelemetryHash1=search.pendingHash1;
                ownershipTelemetryHash2=search.pendingHash2;
                ownershipTelemetryHash3=search.pendingHash3;
                lastNeuralScoreMean=ConvertSideToMoveScoreToWhiteMinusBlack(
                    reader.DecodeUnconditionalScoreMean(),search.pendingPlayer);
                lastNeuralScoreStdev=reader.DecodeUnconditionalScoreStdev();
                lastNeuralLead=ConvertSideToMoveScoreToWhiteMinusBlack(
                    reader.DecodeUnconditionalLead(),search.pendingPlayer);

                // Score/value heads are complete as soon as the packed
                // readback is decoded.  Ownership conversion is a separate
                // presentation pass and must not gate root score readiness.
                search.rootNeuralScoreMean=lastNeuralScoreMean;
                search.rootNeuralScoreStdev=lastNeuralScoreStdev;
                search.rootNeuralLead=lastNeuralLead;
                search.rootNeuralOutputRevision=search.rootRevision;

                // A new root never inherits ownership readiness from the
                // previous root.  It is set only after all 361 points below
                // have been converted.
                lastNeuralOwnershipOutputRevision=0;
                ownershipTelemetryCursor=0;ownershipTelemetrySum=0f;
                ownershipTelemetryActive=reader.ownershipReadbackIncluded&&search.pendingNode==0;
                if(!ownershipTelemetryActive)
                    lastNeuralOutputRevision=search.rootRevision;
                if(search.pendingNode==0)
                {
                    // Preserve all raw heads before handing the reader to
                    // MCTS. Ownership presentation can then continue after
                    // expansion releases the reader channel.
                    CopyRootNeuralSnapshot();
                    float maximum=Mathf.Max(reader.value[0],Mathf.Max(reader.value[1],reader.value[2]));
                    float win=Mathf.Exp(Mathf.Clamp(reader.value[0]-maximum,-80f,80f));
                    float loss=Mathf.Exp(Mathf.Clamp(reader.value[1]-maximum,-80f,80f));
                    float noResult=Mathf.Exp(Mathf.Clamp(reader.value[2]-maximum,-80f,80f));
                    float total=win+loss+noResult;
                    if(total>0f)
                    {
                        float sideWin=win/total;float sideLoss=loss/total;
                        lastNeuralNoResultProbability=noResult/total;
                        lastNeuralValue=sideWin-sideLoss;
                        if(search.pendingPlayer==GoGame.BLACK)
                        { lastNeuralBlackWinProbability=sideWin;lastNeuralWhiteWinProbability=sideLoss; }
                        else
                        { lastNeuralBlackWinProbability=sideLoss;lastNeuralWhiteWinProbability=sideWin; }
                    }
                }
            }

            if(!ownershipTelemetryActive)
            {
                if(search.pendingNode==0)
                {
                    search.rootNeuralOwnershipMean=lastNeuralOwnershipMean;
                }
                return true;
            }
            // Ownership is background presentation. The caller's admitted
            // frame pump schedules it only from remaining budget (or while a
            // readback is genuinely waiting); never mint a second full slice
            // here on the readback-complete path.
            return true;
        }

        private bool StepOwnershipTelemetry(float deadline)
        {
            if(!ownershipTelemetryActive)return true;
            if(search==null||game==null||!ownershipTelemetryInitialized||
                ownershipTelemetryNode!=0||
                search.searchToken!=ownershipTelemetryToken||
                search.rootRevision!=ownershipTelemetryRevision||
                search.rootSettingsRevision!=ownershipTelemetrySettingsRevision||
                search.rootOwnerLifecycle!=ownershipTelemetryOwnerLifecycle||
                game.revision!=ownershipTelemetryRevision||
                ownerLifecycle!=ownershipTelemetryOwnerLifecycle||
                lastRootOutputSearchToken!=ownershipTelemetryToken||
                lastRootOutputRevision!=ownershipTelemetryRevision)
            {
                ownershipTelemetryActive=false;ownershipTelemetryInitialized=false;
                lastNeuralOwnershipOutputRevision=0;
                return false;
            }
            float startedAt=Time.realtimeSinceStartup;
            float remainingBudget=RemainingMilliseconds(deadline);
            if(remainingBudget<=0f)return false;
            int budget=Mathf.Clamp(Mathf.RoundToInt(remainingBudget*128f),16,48);
            int completed=0;
            while(ownershipTelemetryCursor<GoGame.AREA&&completed<budget&&
                (!EncodeTelemetryDeadlineReached(deadline)||completed==0))
            {
                ownershipTelemetrySum+=ConvertSideToMoveOwnershipLogitToWhitePositive(
                    lastRootOwnership[ownershipTelemetryCursor],ownershipTelemetrySide);
                ownershipTelemetryCursor++;completed++;
            }
            if(completed>0)
            {
                ownershipTelemetryPoints+=completed;
                ownershipTelemetryFrames++;
                if(completed>maxOwnershipTelemetryPointsOneFrame)
                    maxOwnershipTelemetryPointsOneFrame=completed;
            }
            float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
            if(elapsed>maxOwnershipTelemetryFrameMilliseconds)
                maxOwnershipTelemetryFrameMilliseconds=elapsed;
            if(ownershipTelemetryCursor<GoGame.AREA)return false;
            lastNeuralOwnershipMean=ownershipTelemetrySum/GoGame.AREA;
            ownershipTelemetryActive=false;
            lastNeuralOutputRevision=ownershipTelemetryRevision;
            lastNeuralOwnershipOutputRevision=ownershipTelemetryRevision;
            if(search.rootRevision==ownershipTelemetryRevision)
                search.rootNeuralOwnershipMean=lastNeuralOwnershipMean;
            return true;
        }

        private bool EncodeTelemetryDeadlineReached(float deadline)
        {
            return Time.realtimeSinceStartup>=deadline;
        }

        public float ConvertSideToMoveScoreToWhiteMinusBlack(float sideToMoveScore,
            int sideToMove)
        {
            return sideToMove==GoGame.BLACK?-sideToMoveScore:sideToMoveScore;
        }

        public float ConvertSideToMoveOwnershipLogitToWhitePositive(float logit,
            int sideToMove)
        {
            float value=Mathf.Clamp(logit,-10f,10f);
            float exponential=Mathf.Exp(value*2f);
            float ownership=(exponential-1f)/(exponential+1f);
            return sideToMove==GoGame.BLACK?-ownership:ownership;
        }

        private bool ReaderMatchesPending()
        {
            return reader.resultToken==search.searchToken&&reader.resultNode==search.pendingNode&&reader.resultRevision==search.rootRevision&&reader.resultSettingsRevision==search.rootSettingsRevision&&reader.resultOwnerLifecycle==search.rootOwnerLifecycle&&reader.resultHash0==search.pendingHash0&&reader.resultHash1==search.pendingHash1&&reader.resultHash2==search.pendingHash2&&reader.resultHash3==search.pendingHash3;
        }

        private bool OwnershipTelemetryMatchesPending()
        {
            return ownershipTelemetryInitialized&&search!=null&&
                ownershipTelemetryToken==search.searchToken&&
                ownershipTelemetryNode==search.pendingNode&&
                ownershipTelemetryRevision==search.rootRevision&&
                ownershipTelemetrySettingsRevision==search.rootSettingsRevision&&
                ownershipTelemetryOwnerLifecycle==search.rootOwnerLifecycle&&
                ownershipTelemetryHash0==search.pendingHash0&&
                ownershipTelemetryHash1==search.pendingHash1&&
                ownershipTelemetryHash2==search.pendingHash2&&
                ownershipTelemetryHash3==search.pendingHash3;
        }

        private bool StepSearch(float deadline)
        {
            if(RemainingMilliseconds(deadline)<=0f)return false;
            float startedAt=Time.realtimeSinceStartup;
            int transitions=0;
            while(transitions<128&&RemainingMilliseconds(deadline)>0f)
            {
                int phaseBefore=search.phase;
                float operationStartedAt=Time.realtimeSinceStartup;
                int phase=search.Step();
                transitions++;
                float operationElapsed=(Time.realtimeSinceStartup-operationStartedAt)*1000f;
                if(phaseBefore==GoMctsSearch.PHASE_BACKING_UP)
                    lastMctsBackupMilliseconds+=operationElapsed;
                else if(phaseBefore==GoMctsSearch.PHASE_RUNNING||
                    phaseBefore==GoMctsSearch.PHASE_SELECTING)
                    lastMctsSelectionMilliseconds+=operationElapsed;
                if(phase!=phaseBefore)sameFramePhaseTransitions++;
                if(phase==GoMctsSearch.PHASE_WAITING_NEURAL||
                    phase==GoMctsSearch.PHASE_COMPLETE||
                    phase==GoMctsSearch.PHASE_CANCELLED)break;
            }
            float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
            lastSearchStepMilliseconds+=elapsed;
            if(elapsed>maxMctsStepMilliseconds)maxMctsStepMilliseconds=elapsed;
            if(search.phase==GoMctsSearch.PHASE_CANCELLED&&search.lastError!="")
            {SetError(search.lastError);return false;}
            if(search.phase==GoMctsSearch.PHASE_WAITING_NEURAL||
                search.phase==GoMctsSearch.PHASE_COMPLETE)return true;
            return RemainingMilliseconds(deadline)>0f;
        }

        private void CommitSearchMove()
        {
            float commitStartedAt=Time.realtimeSinceStartup;
            if(game==null||search==null||search.rootRevision!=game.revision||
            search.rootOwnerLifecycle!=ownerLifecycle)
            {
                SetError("AI search result became stale before commit");
                return;
            }
            if(telemetry!=null)telemetry.CaptureFinalAnalysisDeferred(search,this,
                hintRequested?"AI hint analysis complete":"AI analysis complete");
            if(hintRequested)
            {
                int hintMoveCandidate=search.bestMove;
                hintRequested=false;
                if(game!=null&&game.revision==search.rootRevision&&
                    game.gameState==GoGame.STATE_PLAYING&&game.matchStarted)
                    game.PublishAiHint(hintMoveCandidate);
                PublishDeferredTelemetryAfterMove();
                runtime.ReleaseResources();
                search.CancelSearch();
                lastCommitMilliseconds+=(Time.realtimeSinceStartup-commitStartedAt)*1000f;
                FinishPerformance();
                controllerState=STATE_IDLE;
                lastError="";
                RefreshSurfaces();
                return;
            }
            int before=game.revision;bool committed=false;
            if(activeSearchResignThreshold>-0.9999f&&search.GetRootMeanValue()<=activeSearchResignThreshold)
            {
                game.ResignFromAI();committed=game.revision!=before;
            }
            int move=search.bestMove;
            if(!committed&&game.gameState==GoGame.STATE_PLAYING&&move==GoGame.PASS){game.PassFromAI();committed=game.revision!=before;}
            else if(!committed&&game.gameState==GoGame.STATE_PLAYING&&move>=0&&move<GoGame.AREA)committed=game.TryPlayFromAI(move);
            if(!committed){if(telemetry!=null)telemetry.PublishDeferredFinalAnalysis();SetError("AI selected no legal move: "+search.lastError);return;}
            PublishDeferredTelemetryAfterMove();
            aiMoves++;runtime.ReleaseResources();search.CancelSearch();
            lastCommitMilliseconds+=(Time.realtimeSinceStartup-commitStartedAt)*1000f;
            FinishPerformance();
            controllerState=STATE_IDLE;lastError="";
        }

        private void PublishDeferredTelemetryAfterMove()
        {
            if(telemetry==null||game==null)return;
            telemetry.PublishDeferredFinalAnalysis();
            // The deferred snapshot is the one authoritative telemetry packet
            // for this move. Mark the controller's publication cursor here so
            // RefreshSurfaces does not immediately submit a duplicate packet
            // after GoGame has already serialized the new board revision.
            lastPublishedGameRevision=game.revision;
            lastPublishedSearchPhase=GoMctsSearch.PHASE_IDLE;
            lastPublishedControllerState=STATE_IDLE;
            lastPublishedOwnerLifecycle=ownerLifecycle;
            nextTelemetryPublishTime=Time.realtimeSinceStartup+0.25f;
        }

        private void FinishPerformance()
        {
            if(!performanceSearchActive)return;
            lastSearchCompletedVisits=search==null?0:search.visitsCompleted;
            lastSearchFrames=Time.frameCount-performanceSearchStartedFrame;
            lastSearchMilliseconds=(Time.realtimeSinceStartup-performanceSearchStartedAt)*1000f;
            if(search!=null)
            {
                lastSearchStepCalls=search.searchStepCalls;
                lastLegalMoveChecks=search.legalMoveChecks;
                lastSimulationPlayCalls=search.simulationPlayCalls;
                lastHistoryEntriesExamined=search.simulationState==null?0:
                    search.simulationState.historyEntriesExamined;
                lastHistoryQueryCount=search.simulationState==null?0:
                    search.simulationState.historyQueryCount;
                lastHistoryBucketHitCount=search.simulationState==null?0:
                    search.simulationState.historyBucketHitCount;
                lastHistoryBucketMissCount=search.simulationState==null?0:
                    search.simulationState.historyBucketMissCount;
                lastHistoryFullScanCount=search.simulationState==null?0:
                    search.simulationState.historyFullScanCount;
                lastHistoryMaxEntriesExamined=search.simulationState==null?0:
                    search.simulationState.historyMaxEntriesExamined;
                lastRootCopyMilliseconds=search.lastRootCopyMilliseconds;
                lastNeuralExpansionMilliseconds=search.lastNeuralExpansionMilliseconds;
                lastBackupMilliseconds=search.lastBackupMilliseconds;
                lastScoreUtilityMilliseconds=search.lastScoreUtilityMilliseconds;
            }
            performanceSearchActive=false;
        }

        private void ClearRootNeuralSnapshot()
        {
            lastRootPolicyPass=0f;
            lastRootOutputRevision=0;
            lastRootOutputSearchToken=0;
            lastRootReadbackStages=0;
            lastRootOwnershipReadbacks=0;
            for(int i=0;i<GoGame.AREA;i++)
            {
                lastRootPolicySpatial[i]=0f;
                lastRootOwnership[i]=0f;
            }
            for(int i=0;i<3;i++)lastRootValue[i]=0f;
            for(int i=0;i<4;i++)lastRootScore[i]=0f;
        }

        private void CopyRootNeuralSnapshot()
        {
            if(reader==null||search==null||reader.policySpatial==null||
                reader.policySpatial.Length<GoGame.AREA||reader.ownership==null||
                reader.ownership.Length<GoGame.AREA||reader.value==null||
                reader.value.Length<3||reader.score==null||reader.score.Length<4||
                reader.policyPass==null||reader.policyPass.Length<1)return;
            for(int i=0;i<GoGame.AREA;i++)
            {
                lastRootPolicySpatial[i]=reader.policySpatial[i];
                lastRootOwnership[i]=reader.ownership[i];
            }
            lastRootPolicyPass=reader.policyPass[0];
            for(int i=0;i<3;i++)lastRootValue[i]=reader.value[i];
            for(int i=0;i<4;i++)lastRootScore[i]=reader.score[i];
            lastRootOutputRevision=search.rootRevision;
            lastRootOutputSearchToken=search.searchToken;
            lastRootReadbackStages=reader.completedStages;
            lastRootOwnershipReadbacks=reader.ownershipReadbackCount;
        }

        private void SetError(string message)
        {
            bool wasHint=hintRequested;
            hintRequested=false;
            lastError=message==null?"unknown AI error":message;controllerState=STATE_ERROR;
            if(search!=null&&search.phase!=GoMctsSearch.PHASE_CANCELLED)search.CancelSearch();
            GoGpuNeuralOutputReader invalidatedReader=reader;
            bool abandonedReadback=invalidatedReader!=null&&
                invalidatedReader.readbackState==GoGpuNeuralOutputReader.READBACK_WAITING;
            if(abandonedReadback)invalidatedReader.CancelAsyncReadback();
            if(abandonedReadback||IsReaderQuarantined(invalidatedReader))TryRotateReader();
            if(invalidatedReader!=null)invalidatedReader.ReleaseResources();
            if(encoder!=null)encoder.CancelEncode();
            if(runtime!=null)runtime.ReleaseResources();
            superkoScanActive=false;superkoScanCursor=0;readerPoolWaitStartedAt=0f;
            ownershipTelemetryActive=false;ownershipTelemetryInitialized=false;
            lastNeuralOwnershipOutputRevision=0;
            ownershipTelemetryCursor=0;ownershipTelemetrySum=0f;
            if(wasHint&&game!=null&&game.hintMove!=GoGame.NONE)game.ClearPublishedAiHint();
            FinishPerformance();
            RefreshSurfaces();
        }

        private void RefreshSurfaces()
        {
            if(game==null)return;
            VRCPlayerApi local=Networking.LocalPlayer;
            bool authority=!Utilities.IsValid(local)||Networking.IsOwner(game.gameObject);
            int phase=search==null?-1:search.phase;
            float now=Time.realtimeSinceStartup;
            bool gameOrControllerChanged=lastSurfaceGameRevision!=game.revision||
                lastSurfaceControllerState!=controllerState;
            bool phaseChanged=lastSurfaceSearchPhase!=phase;
            bool liveTelemetry=hintRequested||
                phase==GoMctsSearch.PHASE_WAITING_NEURAL||
                phase==GoMctsSearch.PHASE_EXPANDING||
                phase==GoMctsSearch.PHASE_SELECTING||
                phase==GoMctsSearch.PHASE_BACKING_UP||
                phase==GoMctsSearch.PHASE_RUNNING||
                phase==GoMctsSearch.PHASE_COMPLETE;
            bool searchStartedEvent=phase==GoMctsSearch.PHASE_WAITING_NEURAL&&
                (lastPublishedSearchPhase==GoMctsSearch.PHASE_IDLE||
                 lastPublishedSearchPhase==GoMctsSearch.PHASE_CANCELLED||
                 lastPublishedSearchPhase<0);
            bool searchCompletedEvent=phase==GoMctsSearch.PHASE_COMPLETE&&
                lastPublishedSearchPhase!=GoMctsSearch.PHASE_COMPLETE;
            bool errorEvent=controllerState==STATE_ERROR&&
                lastPublishedControllerState!=STATE_ERROR;
            bool ownershipEvent=ownerLifecycle!=lastPublishedOwnerLifecycle;
            // Internal phase changes remain local presentation state. They no
            // longer force a manual-sync payload; only lifecycle events and the
            // bounded live-search sample publish to the network.
            bool publishDue=authority&&telemetry!=null&&
                (lastPublishedGameRevision!=game.revision||searchStartedEvent||
                 searchCompletedEvent||errorEvent||ownershipEvent||
                 (liveTelemetry&&now>=nextTelemetryPublishTime));
            // Search semantics continue every dispatched frame; only the
            // presentation/telemetry surface is sampled at 10 Hz. This keeps
            // the Xiangqi-style dirty refresh contract without rebuilding TMP
            // strings on every Udon Update.
            // Internal search phases are sampled presentation state. A phase
            // transition alone must not rebuild every TMP panel in the same
            // rendered frame; authoritative game/controller transitions still
            // refresh immediately, and the 10 Hz timer bounds live progress
            // latency for spectators.
            bool presentationDue=gameOrControllerChanged||now>=nextSurfaceRefreshTime;
            if(!presentationDue&&!publishDue&&!phaseChanged)return;
            if(presentationDue)
            {
                nextSurfaceRefreshTime=now+0.10f;
                if(ui!=null)ui.RefreshNow();
            }
            if(telemetry!=null)
            {
                if(publishDue)
                {
                    telemetry.CommitFromController();
                    if(performanceSearchActive)
                    {
                        telemetryCommitsDuringSearch++;
                        telemetrySerializationsDuringSearch++;
                    }
                    lastPublishedGameRevision=game.revision;
                    lastPublishedSearchPhase=phase;
                    lastPublishedControllerState=controllerState;
                    lastPublishedOwnerLifecycle=ownerLifecycle;
                    nextTelemetryPublishTime=now+0.25f;
                }
                if(presentationDue||publishDue)telemetry.RefreshNow();
            }
            lastSurfaceGameRevision=game.revision;
            lastSurfaceSearchPhase=phase;
            lastSurfaceControllerState=controllerState;
        }

        public void SyncControllerMirrorFromGame()
        {
            if(game==null)return;
            if(game.blackIsAI&&game.whiteIsAI)mode=MODE_AIVAI;
            else if(game.blackIsAI)mode=MODE_AIVP;
            else if(game.whiteIsAI)mode=MODE_PVAI;
            else mode=MODE_PVP;
            aiColor=game.blackIsAI?GoGame.BLACK:GoGame.WHITE;
            previousMode=mode;previousAiColor=aiColor;
        }

        private bool TakeOwnership()
        {
            if(!Utilities.IsValid(Networking.LocalPlayer))return true;
            if(!Networking.IsOwner(gameObject))Networking.SetOwner(Networking.LocalPlayer,gameObject);
            return Networking.IsOwner(gameObject);
        }
    }
}
