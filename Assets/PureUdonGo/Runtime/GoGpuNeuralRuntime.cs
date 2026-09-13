using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Fixed production model graph for the committed KataGo V8 network.
    /// This class deliberately exposes the intermediate block outputs so the
    /// graph can be checked layer by layer before it is used by search.
    /// </summary>
    public sealed class GoGpuNeuralRuntime : UdonSharpBehaviour
    {
        public const int NN_IDLE=0;
        public const int NN_INPUT_UPLOAD=1;
        public const int NN_INITIAL_CONV=2;
        public const int NN_RES_BLOCK=3;
        public const int NN_TRUNK_TIP=4;
        public const int NN_POLICY=5;
        public const int NN_VALUE=6;
        public const int NN_SCORE=7;
        public const int NN_OWNERSHIP=8;
        public const int NN_PACK_OUTPUT=9;
        public const int NN_READY_FOR_READBACK=10;
        public const int NN_CANCELLED=11;
        public const int NN_ERROR=12;

        public GoGpuLayerExecutor executor;
        // Opt-in until the reference/fused numerical and chosen-move gates
        // have run. Captured once per evaluation, never changed mid-graph.
        public bool useEquivalentFusion;
        private bool graphUsesFusion;
        public const int REFERENCE_PASSES_WITH_OWNERSHIP=84;
        public const int FUSED_PASSES_WITH_OWNERSHIP=66;
        // Non-root leaves omit the diagnostic ownership head but retain the
        // exact policy/value/score graph. This is the minimum valid pass count
        // for a completed search leaf; it is not a reduced-computation mode.
        public const int FUSED_PASSES_WITHOUT_OWNERSHIP=65;
        public string lastError="";
        public int executedPasses;
        public float lastInputSetupMilliseconds;
        public float lastInputUploadMilliseconds;
        public float lastInputPackingMilliseconds;
        public float lastSpatialApplyMilliseconds;
        public float lastGlobalApplyMilliseconds;
        public float lastInputBlitMilliseconds;
        public float lastGraphDispatchMilliseconds;
        public float lastEvaluationMilliseconds;
        public int gpuGraphStage=NN_IDLE;
        public int gpuGraphStageProgress;
        public int residualBlockIndex;
        public int gpuGraphPassesThisFrame;
        public int gpuGraphFrames;
        public int maxGpuGraphPassesOneFrame;
        public int gpuGraphVisitedStageMask;
        public int completedGpuGraphs;
        public float maxGpuSubmitMsOneFrame;
        public float smoothedGpuSubmitMilliseconds;
        // Full CPU-side elapsed time for the most recent admitted runtime
        // slice.  The scheduler must not use this value as a graph-pass cost:
        // input packing/Texture2D.Apply and upload blits are intentionally
        // reported separately below.
        public float lastSubmitFrameMilliseconds;
        // Persistent scheduler calibration survives BeginGraph/leaf resets.
        // It is intentionally separate from input packing/texture Apply time.
        public float persistentGraphSubmitEmaMs;
        public float persistentGraphPassSubmitEmaMs=0.08f;
        public float inputUploadCpuMilliseconds;
        public float inputBlitSubmitMilliseconds;
        public int graphPassesThisFrame;
        // This is an absolute emergency ceiling supplied by the room
        // scheduler. It is not a second frame-fraction calculation; the
        // admitted controller deadline remains the normal governing limit.
        public float maxGpuSubmitMillisecondsPerFrame=3.00f;
        public float totalGpuSubmitMilliseconds;
        public int cancelledGpuGraphs;
        public int graphTableIdentity=-1;
        public int graphSearchToken=-1;
        public int graphNode=-1;
        public int graphPositionRevision=-1;
        public int graphSettingsRevision=-1;
        public int graphProfileSettingsRevision=-1;
        public int graphOwnerLifecycle=-1;
        public int graphSideToMove;
        public int graphHash0;
        public int graphHash1;
        public int graphHash2;
        public int graphHash3;

        public RenderTexture initialTrunkOutput;
        public RenderTexture[] blockOutputs;
        public RenderTexture trunkTipOutput;
        public RenderTexture policySpatialLogits;
        public RenderTexture policyPassLogit;
        public RenderTexture valueLogits;
        public RenderTexture scoreLogits;
        public RenderTexture ownershipLogits;
        // One coalesced target is used for every leaf. Rows 0..18 contain the
        // spatial heads and the final row contains the vector heads.
        public RenderTexture packedReadbackOutputs;
        public GoFeatureEncoder featureEncoder;

        private RenderTexture[] scratchActivations;
        private int[] scratchActivationChannels;
        private RenderTexture[] scratchVectors;
        private int[] scratchVectorChannels;
        private int scratchActivationCount;
        private int scratchVectorCount;
        private int scratchActivationCursor;
        private int scratchVectorCursor;
        private bool evaluationActive;
        private bool graphIncludeOwnership;
        private float graphEvaluationStartedAt;
        private int inputUploadCursor;
        private float smoothedGpuPassMilliseconds=0.08f;
        private float graphDispatchElapsedThisFrame;
        // A match-start pipeline warm-up uses the exact production graph with
        // deterministic dummy inputs.  It is deliberately separate from a
        // SearchSession: the result is discarded and can never be accepted by
        // MCTS.  The allocated graph textures/material state remain alive for
        // subsequent real leaves.
        public bool pipelineWarmupActive;
        public int pipelineWarmupFrames;
        public int pipelineWarmupPasses;
        public float pipelineWarmupMilliseconds;
        public string pipelineWarmupError="";
        private float pipelineWarmupStartedAt;
        private int pipelineWarmupTableIdentity=-1;
        public int pipelineWarmupToken=-1048576;
        private float[] pipelineWarmupSpatial=new float[GoFeatureEncoder.SPATIAL_COUNT];
        private float[] pipelineWarmupGlobal=new float[GoFeatureEncoder.GLOBAL_CHANNELS];
        private const int PIPELINE_WARMUP_TOKEN=-1048576;
        private RenderTexture graphSpatialInput;
        private RenderTexture graphGlobalInput;
        private RenderTexture graphTrunk;
        private RenderTexture graphA;
        private RenderTexture graphB;
        private RenderTexture graphC;
        private RenderTexture graphD;
        private RenderTexture graphE;
        private RenderTexture graphF;
        private RenderTexture graphG;
        private RenderTexture graphH;
        private RenderTexture graphI;
        private RenderTexture graphPolicyP1;
        private RenderTexture graphPolicyG1;
        private RenderTexture graphPolicyPool;
        private RenderTexture graphPolicyGlobal;
        private RenderTexture graphValueV1;
        private RenderTexture graphValuePool;
        private RenderTexture graphValueHidden;
        private RenderTexture graphValueResult;
        private RenderTexture graphScoreResult;
        private float[] graphSpatialValues;
        private float[] graphGlobalValues;
        private Texture2D spatialUploadTexture;
        private Texture2D globalUploadTexture;
        private Color[] spatialUploadPixels;
        private Color[] globalUploadPixels;
        private RenderTexture spatialUpload;
        private RenderTexture globalUpload;

        public void Start()
        {
            // Generated production instances always use the numerically
            // validated fused graph. Editor numerical verifiers may override
            // this field after loading the scene to run the reference graph.
            useEquivalentFusion=true;
        }

        private int[] ordinaryNorm1Mean;
        private int[] ordinaryNorm1Variance;
        private int[] ordinaryNorm1Bias;
        private int[] ordinaryW1;
        private int[] ordinaryNorm2Mean;
        private int[] ordinaryNorm2Variance;
        private int[] ordinaryNorm2Scale;
        private int[] ordinaryNorm2Bias;
        private int[] ordinaryW2;

        public bool EvaluateGame(GoFeatureEncoder encoder,GoGame game)
        {
            if(encoder==null){lastError="feature encoder is not assigned";return false;}
            if(game==null){lastError="game is not assigned";return false;}
            if(!encoder.Encode(game,encoder.spatialOutput,encoder.globalOutput)){lastError="feature encoding failed: "+encoder.lastError;return false;}
            return EvaluateEncoded(encoder.spatialOutput,encoder.globalOutput);
        }

        public bool EvaluateState(GoFeatureEncoder encoder,int[] board,int[] previousBoard1,int[] previousBoard2,int[] recentMoveLoc,int[] recentMovePla,int sideToMove,int koLoc,int komiTimes2,bool positionalSuperko,bool multiStoneSuicideLegal,bool areaScoring,int consecutivePasses,int gameState,bool[] superkoBanned)
        {
            if(encoder==null){lastError="feature encoder is not assigned";return false;}
            if(!encoder.EncodeState(board,previousBoard1,previousBoard2,recentMoveLoc,recentMovePla,sideToMove,koLoc,komiTimes2,positionalSuperko,multiStoneSuicideLegal,areaScoring,consecutivePasses,gameState,superkoBanned,encoder.spatialOutput,encoder.globalOutput)){lastError="feature encoding failed: "+encoder.lastError;return false;}
            return EvaluateEncoded(encoder.spatialOutput,encoder.globalOutput);
        }

        public bool EvaluateEncoded(float[] spatial,float[] global)
        {
            return EvaluateEncoded(spatial,global,true);
        }

        public bool EvaluateEncoded(float[] spatial,float[] global,bool includeOwnership)
        {
            if(!BeginEvaluateEncoded(spatial,global,includeOwnership,
                -1,-1,-1,-1,-1,-1,-1,0,0,0,0,0))return false;
            int guard=0;
            while(gpuGraphStage!=NN_READY_FOR_READBACK&&gpuGraphStage!=NN_ERROR&&guard<512)
            {
                StepEvaluation(1000f);
                guard++;
            }
            if(gpuGraphStage!=NN_READY_FOR_READBACK)
            {
                if(lastError=="")lastError="synchronous graph wrapper did not complete";
                return false;
            }
            return true;
        }

        public bool Evaluate(RenderTexture spatialInput,RenderTexture globalInput)
        {
            return Evaluate(spatialInput,globalInput,true);
        }

        public bool Evaluate(RenderTexture spatialInput,RenderTexture globalInput,bool includeOwnership)
        {
            if(!BeginEvaluate(spatialInput,globalInput,includeOwnership,
                -1,-1,-1,-1,-1,-1,-1,0,0,0,0,0))return false;
            int guard=0;
            while(gpuGraphStage!=NN_READY_FOR_READBACK&&gpuGraphStage!=NN_ERROR&&guard<256)
            {
                StepEvaluation(1000f);
                guard++;
            }
            return gpuGraphStage==NN_READY_FOR_READBACK;
        }

        public bool BeginEvaluateEncoded(float[] spatial,float[] global,bool includeOwnership,
            int tableIdentity,int searchToken,int node,int positionRevision,
            int settingsRevision,int profileSettingsRevision,int ownerLifecycle,int sideToMove,
            int hash0,int hash1,int hash2,int hash3)
        {
            if(spatial==null||spatial.Length<GoFeatureEncoder.SPATIAL_COUNT)
            {lastError="encoded spatial input is too short";gpuGraphStage=NN_ERROR;return false;}
            if(global==null||global.Length<GoFeatureEncoder.GLOBAL_CHANNELS)
            {lastError="encoded global input is too short";gpuGraphStage=NN_ERROR;return false;}
            float setupStartedAt=Time.realtimeSinceStartup;
            if(!EnsureInputResources()){gpuGraphStage=NN_ERROR;return false;}
            lastInputSetupMilliseconds=(Time.realtimeSinceStartup-setupStartedAt)*1000f;
            if(!BeginGraph(includeOwnership,tableIdentity,searchToken,node,positionRevision,
                settingsRevision,profileSettingsRevision,ownerLifecycle,sideToMove,
                hash0,hash1,hash2,hash3))return false;
            graphSpatialValues=spatial;graphGlobalValues=global;
            graphSpatialInput=spatialUpload;graphGlobalInput=globalUpload;
            gpuGraphStage=NN_INPUT_UPLOAD;gpuGraphStageProgress=0;inputUploadCursor=0;
            return true;
        }

        /// <summary>
        /// Preallocates persistent input/output and fused-graph scratch
        /// resources without dispatching a shader or changing graph identity.
        /// The slot order mirrors the production fused graph so the first
        /// real evaluation reuses every activation/vector texture.
        /// </summary>
        public bool PrepareRuntimeResources()
        {
            lastError="";
            if(executor==null){lastError="GPU executor is not assigned";return false;}
            if(!EnsureInputResources()||!EnsurePackedOutputResources())return false;
            EnsureLayout();
            scratchActivationCursor=0;scratchVectorCursor=0;
            // Keep this allocation order identical to StepOneGraphOperation.
            // NewActivation/NewVector are cursor-backed pools; preallocating
            // the right shapes in a different order would still force a
            // first-search Release/Create churn when a slot's channel count
            // changes. This is preparation only: no dispatch is issued.
            if(NewActivation(128)==null||NewVector(128)==null||
                NewActivation(128)==null)return false;
            for(int block=0;block<10;block++)
            {
                bool gPool=block==4||block==7;
                if(!gPool)
                {
                    if(NewActivation(128)==null||NewActivation(128)==null)return false;
                    if(useEquivalentFusion)
                    {
                        if(NewActivation(128)==null)return false;
                    }
                    else if(NewActivation(128)==null||NewActivation(128)==null||
                        NewActivation(128)==null)return false;
                }
                else
                {
                    if(NewActivation(128)==null||NewActivation(96)==null||
                        NewActivation(32)==null||NewActivation(32)==null||
                        NewVector(96)==null||NewVector(96)==null||
                        NewActivation(96)==null||NewActivation(96)==null||
                        NewActivation(128)==null)return false;
                    if(!useEquivalentFusion&&NewActivation(128)==null)return false;
                }
            }
            if(NewActivation(128)==null)return false;
            if(NewActivation(32)==null||NewActivation(32)==null||
                NewActivation(32)==null||NewVector(96)==null||
                NewVector(32)==null||NewActivation(32)==null||
                NewActivation(32)==null||NewActivation(1)==null||
                NewVector(1)==null)return false;
            if(NewActivation(32)==null||NewActivation(32)==null||
                NewVector(96)==null||NewVector(80)==null||
                NewVector(80)==null||NewVector(3)==null||
                NewVector(3)==null)return false;
            if(NewVector(4)==null||NewVector(4)==null||
                NewActivation(1)==null)return false;
            scratchActivationCursor=0;scratchVectorCursor=0;
            return true;
        }

        /// <summary>
        /// Starts a deterministic production-graph warm-up.  This executes
        /// the same fused graph and ownership packing used by a real root
        /// evaluation, but with zero inputs and a private negative token.  No
        /// search object is involved and the output is never submitted to
        /// PUCT.  The controller consumes the graph in bounded slices before
        /// the first real AI search begins.
        /// </summary>
        public bool BeginPipelineWarmup(int tableIdentity)
        {
            pipelineWarmupError="";
            if(evaluationActive)
            {
                pipelineWarmupError="cannot start pipeline warm-up while a graph is active";
                lastError=pipelineWarmupError;
                return false;
            }
            if(!EnsureInputResources()||!EnsurePackedOutputResources())
            {
                pipelineWarmupError=lastError;
                return false;
            }
            for(int i=0;i<pipelineWarmupSpatial.Length;i++)pipelineWarmupSpatial[i]=0f;
            for(int i=0;i<pipelineWarmupGlobal.Length;i++)pipelineWarmupGlobal[i]=0f;
            pipelineWarmupTableIdentity=tableIdentity;
            pipelineWarmupToken=PIPELINE_WARMUP_TOKEN-
                Mathf.Clamp(tableIdentity,0,GoBoardPool.MAX_TABLES);
            if(pipelineWarmupToken==0)pipelineWarmupToken=PIPELINE_WARMUP_TOKEN;
            pipelineWarmupFrames=0;
            pipelineWarmupPasses=0;
            pipelineWarmupMilliseconds=0f;
            pipelineWarmupStartedAt=Time.realtimeSinceStartup;
            pipelineWarmupActive=true;
            if(!BeginEvaluateEncoded(pipelineWarmupSpatial,pipelineWarmupGlobal,true,
                pipelineWarmupTableIdentity,pipelineWarmupToken,-1,-1,-1,-1,0,
                GoGame.BLACK,0,0,0,0))
            {
                pipelineWarmupActive=false;
                pipelineWarmupError=lastError;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Advances the warm-up graph by one frame-budgeted slice.  The graph
        /// remains in READY_FOR_READBACK when this returns ready so the
        /// controller can exercise the real reader path before releasing it.
        /// </summary>
        public int StepPipelineWarmup(float requestedBudgetMilliseconds)
        {
            if(!pipelineWarmupActive)return gpuGraphStage;
            int previousPasses=executedPasses;
            int state=StepEvaluation(requestedBudgetMilliseconds);
            pipelineWarmupFrames++;
            int submitted=executedPasses-previousPasses;
            if(submitted>0)pipelineWarmupPasses+=submitted;
            if(state==NN_ERROR)
            {
                pipelineWarmupError=lastError;
                pipelineWarmupActive=false;
                pipelineWarmupMilliseconds=(Time.realtimeSinceStartup-
                    pipelineWarmupStartedAt)*1000f;
            }
            else if(state==NN_READY_FOR_READBACK)
            {
                pipelineWarmupActive=false;
                pipelineWarmupMilliseconds=(Time.realtimeSinceStartup-
                    pipelineWarmupStartedAt)*1000f;
            }
            return state;
        }

        public void CancelPipelineWarmup()
        {
            if(!pipelineWarmupActive)return;
            pipelineWarmupActive=false;
            ReleaseResources();
            pipelineWarmupError="cancelled";
            pipelineWarmupMilliseconds=(Time.realtimeSinceStartup-
                pipelineWarmupStartedAt)*1000f;
        }

        public bool BeginEvaluate(RenderTexture spatialInput,RenderTexture globalInput,
            bool includeOwnership,int tableIdentity,int searchToken,int node,
            int positionRevision,int settingsRevision,int profileSettingsRevision,
            int ownerLifecycle,int sideToMove,
            int hash0,int hash1,int hash2,int hash3)
        {
            if(!ValidateInputs(spatialInput,globalInput))
            {gpuGraphStage=NN_ERROR;return false;}
            if(!BeginGraph(includeOwnership,tableIdentity,searchToken,node,positionRevision,
                settingsRevision,profileSettingsRevision,ownerLifecycle,sideToMove,
                hash0,hash1,hash2,hash3))return false;
            graphSpatialValues=null;graphGlobalValues=null;
            graphSpatialInput=spatialInput;graphGlobalInput=globalInput;
            gpuGraphStage=NN_INITIAL_CONV;gpuGraphStageProgress=0;
            return true;
        }

        private bool BeginGraph(bool includeOwnership,int tableIdentity,int searchToken,
            int node,int positionRevision,int settingsRevision,int profileSettingsRevision,
            int ownerLifecycle,
            int sideToMove,int hash0,int hash1,int hash2,int hash3)
        {
            lastError="";executedPasses=0;
            lastInputUploadMilliseconds=0f;lastInputPackingMilliseconds=0f;
            lastSpatialApplyMilliseconds=0f;lastGlobalApplyMilliseconds=0f;
            lastInputBlitMilliseconds=0f;lastGraphDispatchMilliseconds=0f;
            lastEvaluationMilliseconds=0f;totalGpuSubmitMilliseconds=0f;
            gpuGraphPassesThisFrame=0;graphPassesThisFrame=0;gpuGraphFrames=0;maxGpuSubmitMsOneFrame=0f;
            maxGpuGraphPassesOneFrame=0;gpuGraphVisitedStageMask=0;
            graphDispatchElapsedThisFrame=0f;
            inputUploadCpuMilliseconds=0f;inputBlitSubmitMilliseconds=0f;
            if(executor==null){lastError="GPU executor is not assigned";gpuGraphStage=NN_ERROR;return false;}
            if(evaluationActive){lastError="ReleaseResources must be called before starting another evaluation";gpuGraphStage=NN_ERROR;return false;}
            EnsureLayout();scratchActivationCursor=0;scratchVectorCursor=0;
            evaluationActive=true;graphIncludeOwnership=includeOwnership;
            graphUsesFusion=useEquivalentFusion;
            graphEvaluationStartedAt=Time.realtimeSinceStartup;
            graphTableIdentity=tableIdentity;graphSearchToken=searchToken;graphNode=node;
            graphPositionRevision=positionRevision;graphSettingsRevision=settingsRevision;
            graphProfileSettingsRevision=profileSettingsRevision;
            graphOwnerLifecycle=ownerLifecycle;graphSideToMove=sideToMove;
            graphHash0=hash0;graphHash1=hash1;graphHash2=hash2;graphHash3=hash3;
            residualBlockIndex=0;gpuGraphStageProgress=0;
            if(blockOutputs==null||blockOutputs.Length<10)blockOutputs=new RenderTexture[10];
            else for(int i=0;i<blockOutputs.Length;i++)blockOutputs[i]=null;
            initialTrunkOutput=null;trunkTipOutput=null;policySpatialLogits=null;
            policyPassLogit=null;valueLogits=null;scoreLogits=null;ownershipLogits=null;
            ClearGraphTemporaries();
            return true;
        }

        /// <summary>
        /// Advances one slice of the graph using the remaining budget granted
        /// by GoBoardPool/GoAiController. This runtime no longer invents a
        /// second frame-fraction budget; the scheduler is the sole budget
        /// authority and the deadline is the primary throughput limit.
        /// </summary>
        public int StepEvaluation(float requestedBudgetMilliseconds)
        {
            if(!evaluationActive)return gpuGraphStage;
            if(gpuGraphStage==NN_READY_FOR_READBACK||gpuGraphStage==NN_ERROR||
                gpuGraphStage==NN_CANCELLED)return gpuGraphStage;
            float frameStartedAt=Time.realtimeSinceStartup;
            gpuGraphPassesThisFrame=0;graphPassesThisFrame=0;
            graphDispatchElapsedThisFrame=0f;gpuGraphFrames++;
            // The controller owns the frame deadline and passes only its
            // remaining time.  The runtime contributes one absolute emergency
            // ceiling, but must never round a tiny remainder back up to a fresh
            // 0.25ms slice and overshoot the caller's deadline.
            float requested=Mathf.Max(0f,requestedBudgetMilliseconds);
            float safetyCeiling=maxGpuSubmitMillisecondsPerFrame>0f?
                maxGpuSubmitMillisecondsPerFrame:requested;
            float budget=Mathf.Min(requested,Mathf.Max(0.001f,safetyCeiling));
            if(budget<=0f)return gpuGraphStage;
            float deadline=frameStartedAt+budget*0.001f;
            if(gpuGraphStage==NN_INPUT_UPLOAD)
            {
                gpuGraphVisitedStageMask|=1<<NN_INPUT_UPLOAD;
                int uploadSlices=0;
                // Packing and the two texture uploads are CPU-side setup, not
                // independent network evaluations. Continue through adjacent
                // upload stages while the same frame budget remains, stopping
                // only at the real deadline or when the graph enters its first
                // shader stage.
                while(gpuGraphStage==NN_INPUT_UPLOAD&&uploadSlices<32)
                {
                    StepInputUpload();
                    uploadSlices++;
                    if(gpuGraphStage==NN_ERROR||Time.realtimeSinceStartup>=deadline)break;
                }
                if(gpuGraphStage==NN_INPUT_UPLOAD||gpuGraphStage==NN_ERROR)
                {
                    FinishSubmitFrame(frameStartedAt);
                    return gpuGraphStage;
                }
            }
            float estimatedPass=Mathf.Max(0.02f,persistentGraphPassSubmitEmaMs>0f?
                persistentGraphPassSubmitEmaMs:smoothedGpuPassMilliseconds);
            // Prefer the estimated-cost quota, but keep one atomic dispatch as
            // a liveness escape when the estimate is larger than the remaining
            // slice. Otherwise an unusually slow first pass could make the EMA
            // larger than every future slice and starve this graph forever.
            int passQuota=Mathf.FloorToInt(budget/estimatedPass);
            if(passQuota<1)
                passQuota=1;
            while(graphPassesThisFrame<passQuota&&graphPassesThisFrame<128&&
                Time.realtimeSinceStartup<deadline)
            {
                int passesBefore=executedPasses;
                gpuGraphVisitedStageMask|=1<<gpuGraphStage;
                float operationStartedAt=Time.realtimeSinceStartup;
                if(!StepOneGraphOperation())
                {
                    gpuGraphStage=NN_ERROR;evaluationActive=false;
                    break;
                }
                graphDispatchElapsedThisFrame+=(Time.realtimeSinceStartup-operationStartedAt)*1000f;
                int submitted=executedPasses-passesBefore;
                if(submitted>0)
                { graphPassesThisFrame+=submitted;gpuGraphPassesThisFrame+=submitted; }
                if(gpuGraphStage==NN_READY_FOR_READBACK||gpuGraphStage==NN_ERROR)break;
                if(graphPassesThisFrame>0&&Time.realtimeSinceStartup>=deadline)break;
            }
            FinishSubmitFrame(frameStartedAt);
            if(gpuGraphStage==NN_READY_FOR_READBACK)
            {
                lastEvaluationMilliseconds=(Time.realtimeSinceStartup-graphEvaluationStartedAt)*1000f;
                completedGpuGraphs++;
            }
            return gpuGraphStage;
        }

        private void StepInputUpload()
        {
            float startedAt=Time.realtimeSinceStartup;
            if(graphSpatialValues==null||graphGlobalValues==null)
            {
                lastError="cooperative input upload lost its encoded buffers";
                gpuGraphStage=NN_ERROR;evaluationActive=false;return;
            }
            if(gpuGraphStageProgress==0)
            {
                float packingStartedAt=Time.realtimeSinceStartup;
                int end=inputUploadCursor+256;
                int pixelCount=GoGame.AREA*6;
                if(end>pixelCount)end=pixelCount;
                while(inputUploadCursor<end)
                {
                    int group=inputUploadCursor/GoGame.AREA;
                    int loc=inputUploadCursor-group*GoGame.AREA;
                    int channel=group*4;
                    spatialUploadPixels[inputUploadCursor]=new Color(
                        channel<22?graphSpatialValues[channel*GoGame.AREA+loc]:0f,
                        channel+1<22?graphSpatialValues[(channel+1)*GoGame.AREA+loc]:0f,
                        channel+2<22?graphSpatialValues[(channel+2)*GoGame.AREA+loc]:0f,
                        channel+3<22?graphSpatialValues[(channel+3)*GoGame.AREA+loc]:0f);
                    inputUploadCursor++;
                }
                if(inputUploadCursor>=pixelCount)gpuGraphStageProgress=1;
                lastInputPackingMilliseconds+=(Time.realtimeSinceStartup-packingStartedAt)*1000f;
            }
            else if(gpuGraphStageProgress==1)
            {
                float applyStartedAt=Time.realtimeSinceStartup;
                for(int group=0;group<5;group++)
                {
                    int start=group*4;
                    globalUploadPixels[group]=new Color(
                        start<19?graphGlobalValues[start]:0f,
                        start+1<19?graphGlobalValues[start+1]:0f,
                        start+2<19?graphGlobalValues[start+2]:0f,
                        start+3<19?graphGlobalValues[start+3]:0f);
                }
                spatialUploadTexture.SetPixels(spatialUploadPixels);
                spatialUploadTexture.Apply(false,false);
                gpuGraphStageProgress=2;
                lastSpatialApplyMilliseconds+=(Time.realtimeSinceStartup-applyStartedAt)*1000f;
            }
            else if(gpuGraphStageProgress==2)
            {
                float applyStartedAt=Time.realtimeSinceStartup;
                globalUploadTexture.SetPixels(globalUploadPixels);
                globalUploadTexture.Apply(false,false);
                gpuGraphStageProgress=3;
                lastGlobalApplyMilliseconds+=(Time.realtimeSinceStartup-applyStartedAt)*1000f;
            }
            else if(gpuGraphStageProgress==3)
            {
                float blitStartedAt=Time.realtimeSinceStartup;
                VRCGraphics.Blit(spatialUploadTexture,spatialUpload);
                gpuGraphPassesThisFrame++;gpuGraphStageProgress=4;
                float blitMs=(Time.realtimeSinceStartup-blitStartedAt)*1000f;
                lastInputBlitMilliseconds+=blitMs;inputBlitSubmitMilliseconds+=blitMs;
            }
            else
            {
                float blitStartedAt=Time.realtimeSinceStartup;
                VRCGraphics.Blit(globalUploadTexture,globalUpload);
                gpuGraphPassesThisFrame++;gpuGraphStage=NN_INITIAL_CONV;
                gpuGraphStageProgress=0;graphSpatialValues=null;graphGlobalValues=null;
                float blitMs=(Time.realtimeSinceStartup-blitStartedAt)*1000f;
                lastInputBlitMilliseconds+=blitMs;inputBlitSubmitMilliseconds+=blitMs;
            }
            float uploadMs=(Time.realtimeSinceStartup-startedAt)*1000f;
            lastInputUploadMilliseconds+=uploadMs;inputUploadCpuMilliseconds+=uploadMs;
        }

        private bool StepOneGraphOperation()
        {
            gpuGraphVisitedStageMask|=1<<gpuGraphStage;
            if(gpuGraphStage==NN_INITIAL_CONV)
            {
                if(gpuGraphStageProgress==0)graphTrunk=RunConvolution(
                    graphSpatialInput,22,128,5,5,GoProductionModelLayout.Conv1);
                else if(gpuGraphStageProgress==1)graphA=RunMatMul(
                    graphGlobalInput,19,128,GoProductionModelLayout.GInputW);
                else
                {
                    graphTrunk=RunAddVector(graphTrunk,graphA,128);
                    initialTrunkOutput=graphTrunk;
                }
                if((gpuGraphStageProgress==0&&graphTrunk==null)||
                    (gpuGraphStageProgress==1&&graphA==null)||
                    (gpuGraphStageProgress==2&&graphTrunk==null))return false;
                gpuGraphStageProgress++;
                if(gpuGraphStageProgress>=3)
                {gpuGraphStage=NN_RES_BLOCK;gpuGraphStageProgress=0;residualBlockIndex=0;ClearGraphTemporaries();}
                return true;
            }
            if(gpuGraphStage==NN_RES_BLOCK)
            {
                bool ok=residualBlockIndex==4||residualBlockIndex==7
                    ?StepGPoolBlockPass():StepOrdinaryBlockPass();
                if(!ok)return false;
                if(residualBlockIndex>=10)
                {gpuGraphStage=NN_TRUNK_TIP;gpuGraphStageProgress=0;}
                return true;
            }
            if(gpuGraphStage==NN_TRUNK_TIP)
            {
                trunkTipOutput=RunBatchNorm(graphTrunk,128,
                    GoProductionModelLayout.TrunkNormMean,
                    GoProductionModelLayout.TrunkNormVariance,0,
                    GoProductionModelLayout.TrunkNormBias,false);
                if(trunkTipOutput==null)return false;
                gpuGraphStage=NN_POLICY;gpuGraphStageProgress=0;return true;
            }
            if(gpuGraphStage==NN_POLICY)return StepPolicyPass();
            if(gpuGraphStage==NN_VALUE)return StepValuePass();
            if(gpuGraphStage==NN_SCORE)
            {
                if(gpuGraphStageProgress==0)
                    graphScoreResult=RunMatMul(graphValueHidden,80,4,
                        GoProductionModelLayout.ScoreV3W);
                else scoreLogits=RunVectorBias(graphScoreResult,4,
                    GoProductionModelLayout.ScoreV3B,false);
                if((gpuGraphStageProgress==0&&graphScoreResult==null)||
                    (gpuGraphStageProgress==1&&scoreLogits==null))return false;
                gpuGraphStageProgress++;
                if(gpuGraphStageProgress>=2)
                {gpuGraphStage=NN_OWNERSHIP;gpuGraphStageProgress=0;}
                return true;
            }
            if(gpuGraphStage==NN_OWNERSHIP)
            {
                ownershipLogits=null;
                if(graphIncludeOwnership)
                {
                    ownershipLogits=RunConvolution(graphValueV1,32,1,1,1,
                        GoProductionModelLayout.OwnershipW);
                    if(ownershipLogits==null)return false;
                }
                gpuGraphStage=NN_PACK_OUTPUT;gpuGraphStageProgress=0;
                return true;
            }
            if(gpuGraphStage==NN_PACK_OUTPUT)
            {
                if(!EnsurePackedOutputResources())return false;
                if(!executor.DispatchPackReadback(policySpatialLogits,ownershipLogits,
                    policyPassLogit,valueLogits,scoreLogits,packedReadbackOutputs,
                    graphIncludeOwnership))
                {lastError="packed readback output failed: "+executor.lastError;return false;}
                executedPasses++;gpuGraphStage=NN_READY_FOR_READBACK;
                gpuGraphStageProgress=0;return true;
            }
            lastError="unknown GPU graph stage "+gpuGraphStage;
            return false;
        }

        private bool StepOrdinaryBlockPass()
        {
            int index=residualBlockIndex;
            if(graphUsesFusion)
            {
                if(gpuGraphStageProgress==0)graphA=RunBatchNorm(graphTrunk,128,
                    ordinaryNorm1Mean[index],ordinaryNorm1Variance[index],0,
                    ordinaryNorm1Bias[index],false);
                else if(gpuGraphStageProgress==1)graphC=RunConvolutionBatchNorm(graphA,128,128,
                    ordinaryW1[index],ordinaryNorm2Mean[index],ordinaryNorm2Variance[index],
                    ordinaryNorm2Scale[index],ordinaryNorm2Bias[index],true);
                else graphTrunk=RunConvolutionResidual(graphC,graphTrunk,128,128,ordinaryW2[index]);
                RenderTexture fused=gpuGraphStageProgress==0?graphA:
                    gpuGraphStageProgress==1?graphC:graphTrunk;
                if(fused==null)return false;
                gpuGraphStageProgress++;
                if(gpuGraphStageProgress>=3)FinishResidualBlock();
                return true;
            }
            if(gpuGraphStageProgress==0)graphA=RunBatchNorm(graphTrunk,128,
                ordinaryNorm1Mean[index],ordinaryNorm1Variance[index],0,
                ordinaryNorm1Bias[index],false);
            else if(gpuGraphStageProgress==1)graphB=RunConvolution(graphA,128,128,3,3,
                ordinaryW1[index]);
            else if(gpuGraphStageProgress==2)graphC=RunBatchNorm(graphB,128,
                ordinaryNorm2Mean[index],ordinaryNorm2Variance[index],
                ordinaryNorm2Scale[index],ordinaryNorm2Bias[index],true);
            else if(gpuGraphStageProgress==3)graphD=RunConvolution(graphC,128,128,3,3,
                ordinaryW2[index]);
            else graphTrunk=RunAdd(graphTrunk,graphD,128);
            RenderTexture result=gpuGraphStageProgress==0?graphA:
                gpuGraphStageProgress==1?graphB:gpuGraphStageProgress==2?graphC:
                gpuGraphStageProgress==3?graphD:graphTrunk;
            if(result==null)return false;
            gpuGraphStageProgress++;
            if(gpuGraphStageProgress>=5)FinishResidualBlock();
            return true;
        }

        private bool StepGPoolBlockPass()
        {
            bool first=residualBlockIndex==4;
            int norm1Mean=first?GoProductionModelLayout.Rconv5Norm1Mean:GoProductionModelLayout.Rconv8Norm1Mean;
            int norm1Variance=first?GoProductionModelLayout.Rconv5Norm1Variance:GoProductionModelLayout.Rconv8Norm1Variance;
            int norm1Bias=first?GoProductionModelLayout.Rconv5Norm1Bias:GoProductionModelLayout.Rconv8Norm1Bias;
            int w1a=first?GoProductionModelLayout.Rconv5W1A:GoProductionModelLayout.Rconv8W1A;
            int w1b=first?GoProductionModelLayout.Rconv5W1B:GoProductionModelLayout.Rconv8W1B;
            int norm1bMean=first?GoProductionModelLayout.Rconv5Norm1BMean:GoProductionModelLayout.Rconv8Norm1BMean;
            int norm1bVariance=first?GoProductionModelLayout.Rconv5Norm1BVariance:GoProductionModelLayout.Rconv8Norm1BVariance;
            int norm1bBias=first?GoProductionModelLayout.Rconv5Norm1BBias:GoProductionModelLayout.Rconv8Norm1BBias;
            int w1r=first?GoProductionModelLayout.Rconv5W1R:GoProductionModelLayout.Rconv8W1R;
            int norm2Mean=first?GoProductionModelLayout.Rconv5Norm2Mean:GoProductionModelLayout.Rconv8Norm2Mean;
            int norm2Variance=first?GoProductionModelLayout.Rconv5Norm2Variance:GoProductionModelLayout.Rconv8Norm2Variance;
            int norm2Scale=first?GoProductionModelLayout.Rconv5Norm2Scale:GoProductionModelLayout.Rconv8Norm2Scale;
            int norm2Bias=first?GoProductionModelLayout.Rconv5Norm2Bias:GoProductionModelLayout.Rconv8Norm2Bias;
            int w2=first?GoProductionModelLayout.Rconv5W2:GoProductionModelLayout.Rconv8W2;
            if(gpuGraphStageProgress==0)graphA=RunBatchNorm(graphTrunk,128,norm1Mean,norm1Variance,0,norm1Bias,false);
            else if(gpuGraphStageProgress==1)graphB=RunConvolution(graphA,128,96,3,3,w1a);
            else if(gpuGraphStageProgress==2)graphC=RunConvolution(graphA,128,32,3,3,w1b);
            else if(gpuGraphStageProgress==3)graphD=RunBatchNorm(graphC,32,norm1bMean,norm1bVariance,0,norm1bBias,false);
            else if(gpuGraphStageProgress==4)graphE=RunGlobalPool(graphD,32);
            else if(gpuGraphStageProgress==5)graphF=RunMatMul(graphE,96,96,w1r);
            else if(gpuGraphStageProgress==6)graphG=RunAddVector(graphB,graphF,96);
            else if(gpuGraphStageProgress==7)graphH=RunBatchNorm(graphG,96,norm2Mean,norm2Variance,norm2Scale,norm2Bias,true);
            else if(gpuGraphStageProgress==8)
            {
                if(graphUsesFusion)
                {
                    graphTrunk=RunConvolutionResidual(graphH,graphTrunk,96,128,w2);
                    if(graphTrunk==null)return false;
                    FinishResidualBlock();return true;
                }
                graphI=RunConvolution(graphH,96,128,3,3,w2);
            }
            else graphTrunk=RunAdd(graphTrunk,graphI,128);
            RenderTexture result=gpuGraphStageProgress==0?graphA:
                gpuGraphStageProgress==1?graphB:gpuGraphStageProgress==2?graphC:
                gpuGraphStageProgress==3?graphD:gpuGraphStageProgress==4?graphE:
                gpuGraphStageProgress==5?graphF:gpuGraphStageProgress==6?graphG:
                gpuGraphStageProgress==7?graphH:gpuGraphStageProgress==8?graphI:graphTrunk;
            if(result==null)return false;
            gpuGraphStageProgress++;
            if(gpuGraphStageProgress>=10)FinishResidualBlock();
            return true;
        }

        private void FinishResidualBlock()
        {
            blockOutputs[residualBlockIndex]=graphTrunk;
            residualBlockIndex++;gpuGraphStageProgress=0;ClearGraphTemporaries();
        }

        private bool StepPolicyPass()
        {
            if(gpuGraphStageProgress==0)graphPolicyP1=RunConvolution(trunkTipOutput,128,32,1,1,GoProductionModelLayout.PolicyP1W);
            else if(gpuGraphStageProgress==1)graphPolicyG1=RunConvolution(trunkTipOutput,128,32,1,1,GoProductionModelLayout.PolicyG1W);
            else if(gpuGraphStageProgress==2)graphPolicyG1=RunBatchNorm(graphPolicyG1,32,GoProductionModelLayout.PolicyG1NormMean,GoProductionModelLayout.PolicyG1NormVariance,0,GoProductionModelLayout.PolicyG1NormBias,false);
            else if(gpuGraphStageProgress==3)graphPolicyPool=RunGlobalPool(graphPolicyG1,32);
            else if(gpuGraphStageProgress==4)graphPolicyGlobal=RunMatMul(graphPolicyPool,96,32,GoProductionModelLayout.PolicyG2W);
            else if(gpuGraphStageProgress==5)graphPolicyP1=RunAddVector(graphPolicyP1,graphPolicyGlobal,32);
            else if(gpuGraphStageProgress==6)graphPolicyP1=RunBatchNorm(graphPolicyP1,32,GoProductionModelLayout.PolicyP1NormMean,GoProductionModelLayout.PolicyP1NormVariance,0,GoProductionModelLayout.PolicyP1NormBias,false);
            else if(gpuGraphStageProgress==7)policySpatialLogits=RunConvolution(graphPolicyP1,32,1,1,1,GoProductionModelLayout.PolicyP2W);
            else policyPassLogit=RunMatMul(graphPolicyPool,96,1,GoProductionModelLayout.PolicyPassW);
            RenderTexture result=gpuGraphStageProgress<=0?graphPolicyP1:
                gpuGraphStageProgress==1?graphPolicyG1:gpuGraphStageProgress==2?graphPolicyG1:
                gpuGraphStageProgress==3?graphPolicyPool:gpuGraphStageProgress==4?graphPolicyGlobal:
                gpuGraphStageProgress==5?graphPolicyP1:gpuGraphStageProgress==6?graphPolicyP1:
                gpuGraphStageProgress==7?policySpatialLogits:policyPassLogit;
            if(result==null)return false;
            gpuGraphStageProgress++;
            if(gpuGraphStageProgress>=9){gpuGraphStage=NN_VALUE;gpuGraphStageProgress=0;}
            return true;
        }

        private bool StepValuePass()
        {
            if(gpuGraphStageProgress==0)graphValueV1=RunConvolution(trunkTipOutput,128,32,1,1,GoProductionModelLayout.ValueV1W);
            else if(gpuGraphStageProgress==1)graphValueV1=RunBatchNorm(graphValueV1,32,GoProductionModelLayout.ValueV1NormMean,GoProductionModelLayout.ValueV1NormVariance,0,GoProductionModelLayout.ValueV1NormBias,false);
            else if(gpuGraphStageProgress==2)graphValuePool=RunValuePool(graphValueV1,32);
            else if(gpuGraphStageProgress==3)graphValueHidden=RunMatMul(graphValuePool,96,80,GoProductionModelLayout.ValueV2W);
            else if(gpuGraphStageProgress==4)graphValueHidden=RunVectorBias(graphValueHidden,80,GoProductionModelLayout.ValueV2B,true);
            else if(gpuGraphStageProgress==5)graphValueResult=RunMatMul(graphValueHidden,80,3,GoProductionModelLayout.ValueV3W);
            else valueLogits=RunVectorBias(graphValueResult,3,GoProductionModelLayout.ValueV3B,false);
            RenderTexture result=gpuGraphStageProgress<=1?graphValueV1:
                gpuGraphStageProgress==2?graphValuePool:gpuGraphStageProgress<=4?graphValueHidden:
                gpuGraphStageProgress==5?graphValueResult:valueLogits;
            if(result==null)return false;
            gpuGraphStageProgress++;
            if(gpuGraphStageProgress>=7){gpuGraphStage=NN_SCORE;gpuGraphStageProgress=0;}
            return true;
        }

        private void FinishSubmitFrame(float frameStartedAt)
        {
            float elapsed=(Time.realtimeSinceStartup-frameStartedAt)*1000f;
            lastSubmitFrameMilliseconds=elapsed;
            totalGpuSubmitMilliseconds+=elapsed;
            lastGraphDispatchMilliseconds=graphDispatchElapsedThisFrame;
            if(elapsed>maxGpuSubmitMsOneFrame)maxGpuSubmitMsOneFrame=elapsed;
            if(gpuGraphPassesThisFrame>maxGpuGraphPassesOneFrame)
                maxGpuGraphPassesOneFrame=gpuGraphPassesThisFrame;
            if(graphPassesThisFrame>0)
            {
                float graphMs=graphDispatchElapsedThisFrame;
                float perPass=graphMs/graphPassesThisFrame;
                // This is the live scheduler signal.  It deliberately excludes
                // CPU float->Color packing, SetPixels/Apply and input blits,
                // which belong to the separate upload telemetry channels.
                if(smoothedGpuSubmitMilliseconds<=0f)
                    smoothedGpuSubmitMilliseconds=graphMs;
                else
                    smoothedGpuSubmitMilliseconds+=(graphMs-
                        smoothedGpuSubmitMilliseconds)*0.20f;
                smoothedGpuPassMilliseconds+=(perPass-smoothedGpuPassMilliseconds)*0.20f;
                persistentGraphPassSubmitEmaMs+=(perPass-
                    persistentGraphPassSubmitEmaMs)*0.20f;
                if(persistentGraphSubmitEmaMs<=0f)
                    persistentGraphSubmitEmaMs=graphMs;
                else persistentGraphSubmitEmaMs+=(graphMs-
                    persistentGraphSubmitEmaMs)*0.20f;
            }
        }

        public bool MatchesGraphIdentity(int tableIdentity,int searchToken,int node,
            int positionRevision,int settingsRevision,int profileSettingsRevision,
            int ownerLifecycle,int sideToMove,
            int hash0,int hash1,int hash2,int hash3)
        {
            return evaluationActive&&graphTableIdentity==tableIdentity&&
                graphSearchToken==searchToken&&graphNode==node&&
                graphPositionRevision==positionRevision&&
                graphSettingsRevision==settingsRevision&&
                graphProfileSettingsRevision==profileSettingsRevision&&
                graphOwnerLifecycle==ownerLifecycle&&graphSideToMove==sideToMove&&
                graphHash0==hash0&&graphHash1==hash1&&graphHash2==hash2&&graphHash3==hash3;
        }

        private void ClearGraphTemporaries()
        {
            graphA=null;graphB=null;graphC=null;graphD=null;graphE=null;
            graphF=null;graphG=null;graphH=null;graphI=null;
        }

        public void ReleaseResources()
        {
            // Keep the fixed graph's RenderTextures alive between turns. The
            // previous implementation released and recreated roughly one
            // hundred GPU resources for every neural query. Outputs are only
            // cleared after the corresponding async readback has completed;
            // ReleaseAllResources is the explicit teardown path.
            if(evaluationActive&&gpuGraphStage!=NN_READY_FOR_READBACK)
                cancelledGpuGraphs++;
            evaluationActive=false;
            gpuGraphStage=NN_IDLE;gpuGraphStageProgress=0;residualBlockIndex=0;
            scratchActivationCursor=0;
            scratchVectorCursor=0;
            initialTrunkOutput=null;
            trunkTipOutput=null;
            policySpatialLogits=null;
            policyPassLogit=null;
            valueLogits=null;
            scoreLogits=null;
            ownershipLogits=null;
            graphSpatialValues=null;graphGlobalValues=null;
            graphSpatialInput=null;graphGlobalInput=null;graphTrunk=null;
            graphPolicyP1=null;graphPolicyG1=null;graphPolicyPool=null;
            graphPolicyGlobal=null;graphValueV1=null;graphValuePool=null;
            graphValueHidden=null;graphValueResult=null;graphScoreResult=null;
            ClearGraphTemporaries();
        }

        public void ReleaseInputResources()
        {
            if(spatialUpload!=null){spatialUpload.Release();spatialUpload=null;}
            if(globalUpload!=null){globalUpload.Release();globalUpload=null;}
            // These upload mirrors are runtime-created Texture2D objects, not
            // project assets. Clearing only the managed references leaves the
            // native RGBAFloat allocations alive until an unpredictable GC;
            // hidden-table teardown must release them deterministically.
            if(spatialUploadTexture!=null)
            {
                Object.Destroy(spatialUploadTexture);
                spatialUploadTexture=null;
            }
            if(globalUploadTexture!=null)
            {
                Object.Destroy(globalUploadTexture);
                globalUploadTexture=null;
            }
            spatialUploadPixels=null;
            globalUploadPixels=null;
        }

        public void ReleaseAllResources()
        {
            ReleaseResources();
            if(scratchActivations!=null)
            {
                for(int i=0;i<scratchActivationCount;i++)
                {
                    if(scratchActivations[i]!=null)
                    {
                        scratchActivations[i].Release();
                        scratchActivations[i]=null;
                    }
                }
            }
            if(scratchVectors!=null)
            {
                for(int i=0;i<scratchVectorCount;i++)
                {
                    if(scratchVectors[i]!=null)
                    {
                        scratchVectors[i].Release();
                        scratchVectors[i]=null;
                    }
                }
            }
            scratchActivationCount=0;
            scratchVectorCount=0;
            scratchActivationChannels=null;
            scratchVectorChannels=null;
            scratchActivations=null;
            scratchVectors=null;
            if(packedReadbackOutputs!=null){packedReadbackOutputs.Release();packedReadbackOutputs=null;}
            ReleaseInputResources();
        }

        public int GetEstimatedGpuTextureBytes()
        {
            int bytes=0;
            if(scratchActivations!=null)
                for(int i=0;i<scratchActivationCount;i++)
                    if(scratchActivations[i]!=null)
                        bytes+=scratchActivations[i].width*scratchActivations[i].height*16;
            if(scratchVectors!=null)
                for(int i=0;i<scratchVectorCount;i++)
                    if(scratchVectors[i]!=null)
                        bytes+=scratchVectors[i].width*scratchVectors[i].height*16;
            if(spatialUpload!=null)bytes+=spatialUpload.width*spatialUpload.height*16;
            if(globalUpload!=null)bytes+=globalUpload.width*globalUpload.height*16;
            if(packedReadbackOutputs!=null)
                bytes+=packedReadbackOutputs.width*packedReadbackOutputs.height*16;
            // Texture2D upload mirrors and Color[] staging arrays are separate
            // CPU-side RGBAFloat storage with the same 16-byte pixel footprint.
            if(spatialUploadTexture!=null)
                bytes+=spatialUploadTexture.width*spatialUploadTexture.height*16;
            if(globalUploadTexture!=null)
                bytes+=globalUploadTexture.width*globalUploadTexture.height*16;
            if(spatialUploadPixels!=null)bytes+=spatialUploadPixels.Length*16;
            if(globalUploadPixels!=null)bytes+=globalUploadPixels.Length*16;
            return bytes;
        }

        private bool EnsurePackedOutputResources()
        {
            if(packedReadbackOutputs==null)
                packedReadbackOutputs=executor.CreatePackedReadback(GoGame.SIZE,GoGame.SIZE+1);
            if(packedReadbackOutputs==null){lastError=executor.lastError;return false;}
            return true;
        }

        private bool ValidateInputs(RenderTexture spatialInput,RenderTexture globalInput)
        {
            int spatialGroups=(22+3)/4;
            if(spatialInput==null||spatialInput.width!=GoGpuLayerExecutor.BOARD_SIZE||spatialInput.height!=GoGpuLayerExecutor.BOARD_SIZE*spatialGroups)
            {
                lastError="spatial input must be a 19x114 activation texture";
                return false;
            }
            if(globalInput==null||globalInput.height!=1||globalInput.width!=(19+3)/4)
            {
                lastError="global input must be a 5x1 vector texture";
                return false;
            }
            return true;
        }

        private bool EnsureInputResources()
        {
            if(spatialUploadTexture!=null&&globalUploadTexture!=null&&spatialUpload!=null&&globalUpload!=null)return true;
            if(spatialUploadTexture==null)spatialUploadTexture=new Texture2D(GoGpuLayerExecutor.BOARD_SIZE,GoGpuLayerExecutor.BOARD_SIZE*6,TextureFormat.RGBAFloat,false,true);
            if(globalUploadTexture==null)globalUploadTexture=new Texture2D(5,1,TextureFormat.RGBAFloat,false,true);
            if(spatialUploadPixels==null)spatialUploadPixels=new Color[GoGpuLayerExecutor.BOARD_SIZE*GoGpuLayerExecutor.BOARD_SIZE*6];
            if(globalUploadPixels==null)globalUploadPixels=new Color[5];
            spatialUploadTexture.filterMode=FilterMode.Point; spatialUploadTexture.wrapMode=TextureWrapMode.Clamp;
            globalUploadTexture.filterMode=FilterMode.Point; globalUploadTexture.wrapMode=TextureWrapMode.Clamp;
            if(spatialUpload==null)spatialUpload=executor.CreateActivation(22);
            if(globalUpload==null)globalUpload=executor.CreateVector(19);
            if(spatialUpload==null||globalUpload==null){lastError="could not create encoded input RenderTextures";return false;}
            return true;
        }

        private RenderTexture RunConvolution(RenderTexture input,int inputChannels,int outputChannels,int kernelX,int kernelY,int weightBase)
        {
            RenderTexture output=NewActivation(outputChannels);
            if(output==null)return null;
            if(!executor.DispatchConvolution(input,output,inputChannels,outputChannels,kernelX,kernelY,weightBase)){Fail("convolution");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunBatchNorm(RenderTexture input,int channels,int meanBase,int varianceBase,int scaleBase,int biasBase,bool hasScale)
        {
            RenderTexture output=NewActivation(channels);
            if(output==null)return null;
            if(!executor.DispatchBatchNormRelu(input,output,channels,meanBase,varianceBase,scaleBase,biasBase,hasScale,GoProductionModelLayout.BatchNormEpsilon)){Fail("batch norm");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunConvolutionBatchNorm(RenderTexture input,int inputChannels,
            int outputChannels,int weightBase,int meanBase,int varianceBase,int scaleBase,
            int biasBase,bool hasScale)
        {
            RenderTexture output=NewActivation(outputChannels);
            if(output==null)return null;
            if(!executor.DispatchConvolutionBatchNorm(input,output,inputChannels,outputChannels,
                3,3,weightBase,meanBase,varianceBase,scaleBase,biasBase,hasScale))
            {Fail("convolution + batch norm");return null;}
            executedPasses++;return output;
        }

        private RenderTexture RunConvolutionResidual(RenderTexture input,RenderTexture residual,
            int inputChannels,int outputChannels,int weightBase)
        {
            RenderTexture output=NewActivation(outputChannels);
            if(output==null)return null;
            if(!executor.DispatchConvolutionResidual(input,residual,output,inputChannels,
                outputChannels,3,3,weightBase)){Fail("convolution + residual");return null;}
            executedPasses++;return output;
        }

        private RenderTexture RunAdd(RenderTexture input,RenderTexture residual,int channels)
        {
            RenderTexture output=NewActivation(channels);
            if(output==null)return null;
            if(!executor.DispatchAdd(input,residual,output,channels)){Fail("residual add");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunAddVector(RenderTexture input,RenderTexture vector,int channels)
        {
            RenderTexture output=NewActivation(channels);
            if(output==null)return null;
            if(!executor.DispatchAddVectorToActivation(input,vector,output,channels)){Fail("vector injection");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunGlobalPool(RenderTexture input,int channels)
        {
            RenderTexture output=NewVector(channels*3);
            if(output==null)return null;
            if(!executor.DispatchGlobalPool(input,output,channels)){Fail("global pool");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunValuePool(RenderTexture input,int channels)
        {
            RenderTexture output=NewVector(channels*3);
            if(output==null)return null;
            if(!executor.DispatchValuePool(input,output,channels)){Fail("value pool");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunMatMul(RenderTexture input,int inputChannels,int outputChannels,int weightBase)
        {
            RenderTexture output=NewVector(outputChannels);
            if(output==null)return null;
            if(!executor.DispatchMatMul(input,output,inputChannels,outputChannels,weightBase)){Fail("matmul");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunVectorBias(RenderTexture input,int channels,int biasBase,bool relu)
        {
            RenderTexture output=NewVector(channels);
            if(output==null)return null;
            if(!executor.DispatchVectorBias(input,output,channels,biasBase,relu)){Fail("vector bias");return null;}
            executedPasses++;
            return output;
        }

        private RenderTexture RunOrdinaryBlock(RenderTexture trunk,int index)
        {
            RenderTexture pre=RunBatchNorm(trunk,128,ordinaryNorm1Mean[index],ordinaryNorm1Variance[index],0,ordinaryNorm1Bias[index],false);
            if(pre==null)return null;
            RenderTexture first=RunConvolution(pre,128,128,3,3,ordinaryW1[index]);
            if(first==null)return null;
            RenderTexture normalized=RunBatchNorm(first,128,ordinaryNorm2Mean[index],ordinaryNorm2Variance[index],ordinaryNorm2Scale[index],ordinaryNorm2Bias[index],true);
            if(normalized==null)return null;
            RenderTexture second=RunConvolution(normalized,128,128,3,3,ordinaryW2[index]);
            if(second==null)return null;
            RenderTexture result=RunAdd(trunk,second,128);
            if(result!=null)blockOutputs[index]=result;
            return result;
        }

        private RenderTexture RunGPoolBlock(RenderTexture trunk,int index)
        {
            bool first=index==4;
            int norm1Mean=first?GoProductionModelLayout.Rconv5Norm1Mean:GoProductionModelLayout.Rconv8Norm1Mean;
            int norm1Variance=first?GoProductionModelLayout.Rconv5Norm1Variance:GoProductionModelLayout.Rconv8Norm1Variance;
            int norm1Bias=first?GoProductionModelLayout.Rconv5Norm1Bias:GoProductionModelLayout.Rconv8Norm1Bias;
            int w1a=first?GoProductionModelLayout.Rconv5W1A:GoProductionModelLayout.Rconv8W1A;
            int w1b=first?GoProductionModelLayout.Rconv5W1B:GoProductionModelLayout.Rconv8W1B;
            int norm1bMean=first?GoProductionModelLayout.Rconv5Norm1BMean:GoProductionModelLayout.Rconv8Norm1BMean;
            int norm1bVariance=first?GoProductionModelLayout.Rconv5Norm1BVariance:GoProductionModelLayout.Rconv8Norm1BVariance;
            int norm1bBias=first?GoProductionModelLayout.Rconv5Norm1BBias:GoProductionModelLayout.Rconv8Norm1BBias;
            int w1r=first?GoProductionModelLayout.Rconv5W1R:GoProductionModelLayout.Rconv8W1R;
            int norm2Mean=first?GoProductionModelLayout.Rconv5Norm2Mean:GoProductionModelLayout.Rconv8Norm2Mean;
            int norm2Variance=first?GoProductionModelLayout.Rconv5Norm2Variance:GoProductionModelLayout.Rconv8Norm2Variance;
            int norm2Scale=first?GoProductionModelLayout.Rconv5Norm2Scale:GoProductionModelLayout.Rconv8Norm2Scale;
            int norm2Bias=first?GoProductionModelLayout.Rconv5Norm2Bias:GoProductionModelLayout.Rconv8Norm2Bias;
            int w2=first?GoProductionModelLayout.Rconv5W2:GoProductionModelLayout.Rconv8W2;

            RenderTexture pre=RunBatchNorm(trunk,128,norm1Mean,norm1Variance,0,norm1Bias,false);
            if(pre==null)return null;
            RenderTexture regular=RunConvolution(pre,128,96,3,3,w1a);
            if(regular==null)return null;
            RenderTexture pooledBranch=RunConvolution(pre,128,32,3,3,w1b);
            if(pooledBranch==null)return null;
            pooledBranch=RunBatchNorm(pooledBranch,32,norm1bMean,norm1bVariance,0,norm1bBias,false);
            if(pooledBranch==null)return null;
            RenderTexture pooled=RunGlobalPool(pooledBranch,32);
            if(pooled==null)return null;
            RenderTexture projected=RunMatMul(pooled,96,96,w1r);
            if(projected==null)return null;
            regular=RunAddVector(regular,projected,96);
            if(regular==null)return null;
            regular=RunBatchNorm(regular,96,norm2Mean,norm2Variance,norm2Scale,norm2Bias,true);
            if(regular==null)return null;
            RenderTexture second=RunConvolution(regular,96,128,3,3,w2);
            if(second==null)return null;
            RenderTexture result=RunAdd(trunk,second,128);
            if(result!=null)blockOutputs[index]=result;
            return result;
        }

        private RenderTexture NewActivation(int channels)
        {
            if(scratchActivations==null)
            {
                scratchActivations=new RenderTexture[128];
                scratchActivationChannels=new int[128];
            }
            if(scratchActivationCursor>=scratchActivations.Length){lastError="activation scratch capacity exhausted";return null;}
            int slot=scratchActivationCursor++;
            if(scratchActivations[slot]==null||scratchActivationChannels[slot]!=channels)
            {
                if(scratchActivations[slot]!=null)scratchActivations[slot].Release();
                scratchActivations[slot]=executor.CreateActivation(channels);
                scratchActivationChannels[slot]=channels;
                if(scratchActivations[slot]==null){lastError=executor.lastError;return null;}
            }
            if(slot>=scratchActivationCount)scratchActivationCount=slot+1;
            return scratchActivations[slot];
        }

        private RenderTexture NewVector(int channels)
        {
            if(scratchVectors==null)
            {
                scratchVectors=new RenderTexture[64];
                scratchVectorChannels=new int[64];
            }
            if(scratchVectorCursor>=scratchVectors.Length){lastError="vector scratch capacity exhausted";return null;}
            int slot=scratchVectorCursor++;
            if(scratchVectors[slot]==null||scratchVectorChannels[slot]!=channels)
            {
                if(scratchVectors[slot]!=null)scratchVectors[slot].Release();
                scratchVectors[slot]=executor.CreateVector(channels);
                scratchVectorChannels[slot]=channels;
                if(scratchVectors[slot]==null){lastError=executor.lastError;return null;}
            }
            if(slot>=scratchVectorCount)scratchVectorCount=slot+1;
            return scratchVectors[slot];
        }

        private void Fail(string operation)
        {
            lastError=operation+" failed: "+executor.lastError;
        }

        private void EnsureLayout()
        {
            if(ordinaryW1!=null)return;
            ordinaryNorm1Mean=new int[]{GoProductionModelLayout.Rconv1Norm1Mean,GoProductionModelLayout.Rconv2Norm1Mean,GoProductionModelLayout.Rconv3Norm1Mean,GoProductionModelLayout.Rconv4Norm1Mean,0,GoProductionModelLayout.Rconv6Norm1Mean,GoProductionModelLayout.Rconv7Norm1Mean,0,GoProductionModelLayout.Rconv9Norm1Mean,GoProductionModelLayout.Rconv10Norm1Mean};
            ordinaryNorm1Variance=new int[]{GoProductionModelLayout.Rconv1Norm1Variance,GoProductionModelLayout.Rconv2Norm1Variance,GoProductionModelLayout.Rconv3Norm1Variance,GoProductionModelLayout.Rconv4Norm1Variance,0,GoProductionModelLayout.Rconv6Norm1Variance,GoProductionModelLayout.Rconv7Norm1Variance,0,GoProductionModelLayout.Rconv9Norm1Variance,GoProductionModelLayout.Rconv10Norm1Variance};
            ordinaryNorm1Bias=new int[]{GoProductionModelLayout.Rconv1Norm1Bias,GoProductionModelLayout.Rconv2Norm1Bias,GoProductionModelLayout.Rconv3Norm1Bias,GoProductionModelLayout.Rconv4Norm1Bias,0,GoProductionModelLayout.Rconv6Norm1Bias,GoProductionModelLayout.Rconv7Norm1Bias,0,GoProductionModelLayout.Rconv9Norm1Bias,GoProductionModelLayout.Rconv10Norm1Bias};
            ordinaryW1=new int[]{GoProductionModelLayout.Rconv1W1,GoProductionModelLayout.Rconv2W1,GoProductionModelLayout.Rconv3W1,GoProductionModelLayout.Rconv4W1,0,GoProductionModelLayout.Rconv6W1,GoProductionModelLayout.Rconv7W1,0,GoProductionModelLayout.Rconv9W1,GoProductionModelLayout.Rconv10W1};
            ordinaryNorm2Mean=new int[]{GoProductionModelLayout.Rconv1Norm2Mean,GoProductionModelLayout.Rconv2Norm2Mean,GoProductionModelLayout.Rconv3Norm2Mean,GoProductionModelLayout.Rconv4Norm2Mean,0,GoProductionModelLayout.Rconv6Norm2Mean,GoProductionModelLayout.Rconv7Norm2Mean,0,GoProductionModelLayout.Rconv9Norm2Mean,GoProductionModelLayout.Rconv10Norm2Mean};
            ordinaryNorm2Variance=new int[]{GoProductionModelLayout.Rconv1Norm2Variance,GoProductionModelLayout.Rconv2Norm2Variance,GoProductionModelLayout.Rconv3Norm2Variance,GoProductionModelLayout.Rconv4Norm2Variance,0,GoProductionModelLayout.Rconv6Norm2Variance,GoProductionModelLayout.Rconv7Norm2Variance,0,GoProductionModelLayout.Rconv9Norm2Variance,GoProductionModelLayout.Rconv10Norm2Variance};
            ordinaryNorm2Scale=new int[]{GoProductionModelLayout.Rconv1Norm2Scale,GoProductionModelLayout.Rconv2Norm2Scale,GoProductionModelLayout.Rconv3Norm2Scale,GoProductionModelLayout.Rconv4Norm2Scale,0,GoProductionModelLayout.Rconv6Norm2Scale,GoProductionModelLayout.Rconv7Norm2Scale,0,GoProductionModelLayout.Rconv9Norm2Scale,GoProductionModelLayout.Rconv10Norm2Scale};
            ordinaryNorm2Bias=new int[]{GoProductionModelLayout.Rconv1Norm2Bias,GoProductionModelLayout.Rconv2Norm2Bias,GoProductionModelLayout.Rconv3Norm2Bias,GoProductionModelLayout.Rconv4Norm2Bias,0,GoProductionModelLayout.Rconv6Norm2Bias,GoProductionModelLayout.Rconv7Norm2Bias,0,GoProductionModelLayout.Rconv9Norm2Bias,GoProductionModelLayout.Rconv10Norm2Bias};
            ordinaryW2=new int[]{GoProductionModelLayout.Rconv1W2,GoProductionModelLayout.Rconv2W2,GoProductionModelLayout.Rconv3W2,GoProductionModelLayout.Rconv4W2,0,GoProductionModelLayout.Rconv6W2,GoProductionModelLayout.Rconv7W2,0,GoProductionModelLayout.Rconv9W2,GoProductionModelLayout.Rconv10W2};
        }
    }
}
