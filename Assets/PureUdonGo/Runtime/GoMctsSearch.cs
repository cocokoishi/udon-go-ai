using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Fixed-capacity PUCT/MCTS controller. The tree stores policy priors from
    /// the neural network and backs up the neural value plus a bounded decoded
    /// score utility with alternating player perspective. Neural results carry
    /// search/revision/position identity so late results cannot mutate a new
    /// search.
    /// </summary>
    public sealed class GoMctsSearch : UdonSharpBehaviour
    {
        public const int MAX_NODES=2048;
        // A full 19x19 policy expansion can contain 361 placements plus pass.
        // Keep enough edge storage for the maximum visit count accepted below,
        // including the root expansion and one leaf expansion per visit.
        // 362 policy edges are reserved for the root and each additional visit.
        // 362 * 1227 = 444174, so the fixed tree accepts exactly 1226 visits.
        public const int MAX_EDGES=444174;
        public const int MAX_PATH_DEPTH=512;
        public const int POLICY_SIZE=GoGame.AREA+1;
        // One full policy expansion is reserved for the root and one for each
        // additional visit. This is the actual maximum accepted by the fixed
        // Udon tree, so the public Ultrahard profile cannot overstate strength.
        public const int MAX_SUPPORTED_VISITS=MAX_EDGES/POLICY_SIZE-1;
        public const int PHASE_IDLE=0;
        public const int PHASE_WAITING_NEURAL=1;
        public const int PHASE_RUNNING=2;
        public const int PHASE_COMPLETE=3;
        public const int PHASE_CANCELLED=4;
        public const int PHASE_EXPANDING=5;
        public const int PHASE_SELECTING=6;
        public const int PHASE_BACKING_UP=7;
        // Optional diagnostic ablation bits. Zero is the shipped product
        // semantics; verifiers can enable one bit at a time without changing
        // public difficulty settings or temperature behaviour.
        public const int SEMANTICS_KATAGO_FPU=1;
        public const int SEMANTICS_KATAGO_DYNAMIC_CPUCT=2;
        public const int SEMANTICS_KATAGO_RECENT_SCORE_CENTER=4;
        public const int SEMANTICS_KATAGO_RULE_AWARE_NO_RESULT=8;
        public const int SEMANTICS_LEGACY=0;
        public const int REFERENCE_DECISION_CANDIDATE_COUNT=8;

        public GoSearchState simulationState;
        public GoGame authoritativeGame;
        public int targetVisits=GoDifficultyProfile.BEGINNER_VISITS;
        public float explorationConstant=1.4f;
        public int phase=PHASE_IDLE;
        public int visitsCompleted;
        public int treeNodeCount;
        public int treeEdgeCount;
        public int bestMove=GoGame.NONE;
        public int searchToken;
        public int rootRevision;
        public int rootGameSettingsRevision;
        public int rootSettingsRevision;
        public int rootOwnerLifecycle;
        public float rootMoveTemperature=0.01f;
        public int rootPolicyTopK=POLICY_SIZE;
        public int rootSelectionSeed=1831565813;
        public float rootNeuralScoreMean;
        public float rootNeuralScoreStdev;
        public float rootNeuralLead;
        public float rootNeuralOwnershipMean;
        public int rootNeuralOutputRevision;
        public int pendingNode=-1;
        public int pendingPlayer;
        public int pendingHash0;
        public int pendingHash1;
        public int pendingHash2;
        public int pendingHash3;
        public int staleResultsDiscarded;
        public string lastError="";
        public float lastRootCopyMilliseconds;
        public float lastNeuralExpansionMilliseconds;
        public float lastBackupMilliseconds;
        public float lastScoreUtilityMilliseconds;
        public int searchStepCalls;
        public int legalMoveChecks;
        public int simulationPlayCalls;
        public int pendingExpansionStage;
        public int pendingExpansionCursor;
        public int pendingExpansionLegalCount;
        public int scoreUtilitySamples;
        public int maxScoreUtilitySamplesOneStep;
        public int rootStateCopiedInts;
        public int rootHistoryCopiedEntries;
        public int simulationUndoOperations;
        public int simulationRootResets;
        public int searchSemanticsMode;
        public float recentScoreCenter;
        public float lastFpuValue;
        public float lastExplorationCoefficient;
        public int fpuSelectionCount;
        public int dynamicCpuctSelectionCount;
        public int recentScoreCenterUseCount;
        public int ruleAwareNoResultCount;

        // Optional desktop-reference tape.  It is disabled and unallocated in
        // production; a verifier may enable it for a tiny search fixture to
        // capture the exact legal mask and NN heads consumed at each expansion.
        public bool referenceTraceEnabled;
        public int referenceTraceCapacity;
        public int referenceTraceExpansionCount;
        public int[] referenceTraceNodeIds;
        public int[] referenceTraceParentNodeIds;
        public int[] referenceTraceParentMoves;
        public int[] referenceTraceExpansionParentNodeIds;
        public int[] referenceTraceExpansionParentMoves;
        public int[] referenceTraceLegalMask;
        public float[] referenceTracePolicySpatial;
        public float[] referenceTracePolicyPass;
        public float[] referenceTraceValue0;
        public float[] referenceTraceValue1;
        public float[] referenceTraceValue2;
        public float[] referenceTraceScoreMean;
        public float[] referenceTraceScoreStdev;

        // Bounded, opt-in root decision tape used to localize the first
        // simulation branch that differs from the KataGo oracle. These arrays
        // stay null in production and contain no strings or per-edge objects.
        public int referenceDecisionCount;
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
        private int[] referenceDecisionScratchEdges=new int[REFERENCE_DECISION_CANDIDATE_COUNT];
        private float[] referenceDecisionScratchScores=new float[REFERENCE_DECISION_CANDIDATE_COUNT];

        // Node metadata is also heavy when serialized for all sixteen backing
        // tables. Keep the fixed capacity, but allocate it only when a table
        // actually starts a search, just like the edge storage below.
        public int[] nodeFirstEdge;
        public int[] nodeEdgeCount;
        public int[] nodeVisits;
        public int[] nodeExpanded;
        public int[] nodeTerminal;
        public float[] nodeValueSum;
        public bool nodeStorageAllocated;
        // Edge storage is the dominant per-table allocation (~10 MiB raw).
        // Hidden pool entries must not pay that cost before their first search;
        // EnsureEdgeStorage allocates the same fixed capacity lazily when the
        // table becomes active. The capacity and search semantics do not change.
        public int[] edgeChild;
        public int[] edgeMove;
        public int[] edgeVisits;
        public float[] edgePrior;
        public float[] edgeValueSum;
        public bool edgeStorageAllocated;

        private int[] rootBoard=new int[GoGame.AREA];
        private int[] rootPreviousBoard1=new int[GoGame.AREA];
        private int[] rootPreviousBoard2=new int[GoGame.AREA];
        private int[] rootRecentMoveLoc=new int[5];
        private int[] rootRecentMovePla=new int[5];
        private int rootHistoryStoneCountVersion;
        private int rootPositionHistoryCount;
        private int rootSideToMove;
        private int rootMoveCount;
        private int rootConsecutivePasses;
        private int rootBlackCaptures;
        private int rootWhiteCaptures;
        private int rootGameState;
        private int rootWinner;
        private int rootLastMove;
        private int rootKoLoc;
        private int rootHandicapStones;
        private int rootKomiTimes2;
        private bool rootPositionalSuperko;
        private bool rootAreaScoring;
        private bool rootMultiStoneSuicideLegal;
        private int rootCurrentHash0;
        private int rootCurrentHash1;
        private int rootCurrentHash2;
        private int rootCurrentHash3;
        private int rootCurrentStoneCount;

        private int[] pathNodes=new int[MAX_PATH_DEPTH];
        private int[] pathEdges=new int[MAX_PATH_DEPTH];
        private int[] rootSelectionEdges=new int[POLICY_SIZE];
        private float[] rootSelectionWeights=new float[POLICY_SIZE];
        private int[] legalMoves=new int[GoGame.AREA];
        private float[] pendingPolicySpatial=new float[GoGame.AREA];
        private float pendingPolicyPass;
        private float pendingValue0;
        private float pendingValue1;
        private float pendingValue2;
        private float pendingScoreMean;
        private float pendingScoreStdev;
        private int pendingResultRevision;
        private int pendingExpansionNode=-1;
        private int pendingExpansionFirstEdge;
        private int pendingExpansionCount;
        private float pendingExpansionMaximum;
        private float pendingExpansionTotal;
        private float pendingExpansionStartedAt;
        private int pendingExpansionSubmittedNode;
        private float pendingEffectiveScoreMean;
        private float pendingEffectiveScoreStdev;
        private int pendingScoreSampleIndex;
        private float pendingScoreWeightTotal;
        private float pendingStaticScoreWeighted;
        private float pendingDynamicScoreWeighted;
        // The 101-point normal quadrature is part of the current score utility
        // semantics. Cache only its fixed Gaussian weights so each expansion
        // avoids recomputing 202 identical Exp calls; Atan and all score
        // arithmetic remain per-sample and unchanged.
        private float[] scoreUtilityWeights=new float[101];
        private bool scoreUtilityWeightsReady;
        private int pathDepth;
        private bool selectionActive;
        private int selectionNode;
        private int backupDepth;
        private float backupValue;
        private float backupStartedAt;
        // A single edge replay includes a full Go legality/capture update.
        // Keep the Udon work unit at one edge; GoAiController may admit more
        // such units only while the measured frame deadline still permits it.
        private const int SELECTION_EDGES_PER_STEP=1;
        private const float KATAGO_FPU_REDUCTION_MAX=0.20f;
        private const float KATAGO_CPUCT_LOG=0f;
        private const float KATAGO_CPUCT_BASE=500f;

        public void ConfigureRootSelection(float temperature,int topK,int seed)
        {
            rootMoveTemperature=Mathf.Clamp(temperature,0.01f,100f);
            rootPolicyTopK=Mathf.Clamp(topK,1,POLICY_SIZE);
            rootSelectionSeed=seed==0?1831565813:seed;
        }

        /// <summary>Configures opt-in semantic ablation bits for diagnostics.</summary>
        public void ConfigureSearchSemantics(int mode)
        { searchSemanticsMode=Mathf.Clamp(mode,0,15); }

        /// <summary>
        /// Allocates an opt-in, bounded expansion tape for the desktop Udon
        /// search reference. This is diagnostic-only and never runs unless a
        /// verifier explicitly enables it.
        /// </summary>
        public bool ConfigureReferenceTrace(int capacity)
        {
            capacity=Mathf.Clamp(capacity,1,MAX_NODES);
            referenceTraceCapacity=capacity;
            referenceTraceExpansionCount=0;
            referenceTraceNodeIds=new int[capacity];
            referenceTraceParentNodeIds=new int[capacity];
            referenceTraceParentMoves=new int[capacity];
            referenceTraceExpansionParentNodeIds=new int[capacity];
            referenceTraceExpansionParentMoves=new int[capacity];
            referenceTraceLegalMask=new int[capacity*(GoGame.AREA+1)];
            referenceTracePolicySpatial=new float[capacity*GoGame.AREA];
            referenceTracePolicyPass=new float[capacity];
            referenceTraceValue0=new float[capacity];
            referenceTraceValue1=new float[capacity];
            referenceTraceValue2=new float[capacity];
            referenceTraceScoreMean=new float[capacity];
            referenceTraceScoreStdev=new float[capacity];
            referenceDecisionCount=0;
            referenceDecisionSimulationIndex=new int[capacity];
            referenceDecisionParentVisits=new int[capacity];
            referenceDecisionSelectedMove=new int[capacity];
            referenceDecisionSelectedEdgeVisits=new int[capacity];
            referenceDecisionPolicyMassVisited=new float[capacity];
            referenceDecisionTotalChildWeight=new float[capacity];
            referenceDecisionFpu=new float[capacity];
            referenceDecisionExploration=new float[capacity];
            referenceDecisionBestScore=new float[capacity];
            referenceDecisionSecondBestScore=new float[capacity];
            referenceDecisionSelectedPrior=new float[capacity];
            referenceDecisionSelectedQ=new float[capacity];
            referenceDecisionSelectedU=new float[capacity];
            referenceDecisionSelectedScore=new float[capacity];
            int candidateCapacity=capacity*REFERENCE_DECISION_CANDIDATE_COUNT;
            referenceDecisionCandidateMoves=new int[candidateCapacity];
            referenceDecisionCandidateVisits=new int[candidateCapacity];
            referenceDecisionCandidatePrior=new float[candidateCapacity];
            referenceDecisionCandidateQ=new float[candidateCapacity];
            referenceDecisionCandidateU=new float[candidateCapacity];
            referenceDecisionCandidateScore=new float[candidateCapacity];
            referenceTraceEnabled=true;
            for(int i=0;i<capacity;i++)
            {
                referenceTraceNodeIds[i]=-1;
                referenceTraceParentNodeIds[i]=-1;
                referenceTraceParentMoves[i]=GoGame.NONE;
                referenceTraceExpansionParentNodeIds[i]=-1;
                referenceTraceExpansionParentMoves[i]=GoGame.NONE;
                referenceDecisionSimulationIndex[i]=-1;
                referenceDecisionSelectedMove[i]=GoGame.NONE;
            }
            for(int i=0;i<candidateCapacity;i++)referenceDecisionCandidateMoves[i]=GoGame.NONE;
            return true;
        }

        public void DisableReferenceTrace()
        { referenceTraceEnabled=false; }

        public bool BeginSearch(GoGame game,int visits,int ownerLifecycle)
        {
            return BeginSearch(game,visits,ownerLifecycle,game==null?0:game.settingsRevision);
        }

        /// <summary>
        /// Allocates the fixed search backing arrays without creating a search
        /// session or changing any root/search identity.  The controller uses
        /// this during the owning table's pre-match warm-up.
        /// </summary>
        public bool PrepareRuntimeStorage()
        {
            lastError="";
            if(!EnsureNodeStorage())
            {
                lastError="MCTS node storage could not be allocated";
                return false;
            }
            if(!EnsureEdgeStorage())
            {
                lastError="MCTS edge storage could not be allocated";
                return false;
            }
            EnsureScoreUtilityWeights();
            return true;
        }

        public bool BeginSearch(GoGame game,int visits,int ownerLifecycle,int requestedSettingsRevision)
        {
            lastError="";
            if(game==null){lastError="authoritative game is null";phase=PHASE_CANCELLED;return false;}
            if(simulationState==null){lastError="simulation state is not assigned";phase=PHASE_CANCELLED;return false;}
            if(!EnsureNodeStorage())
            {
                lastError="MCTS node storage could not be allocated";
                phase=PHASE_CANCELLED;return false;
            }
            if(!EnsureEdgeStorage())
            {
                lastError="MCTS edge storage could not be allocated";
                phase=PHASE_CANCELLED;return false;
            }
            if(visits<1)visits=1;
            int maximumVisits=MAX_NODES-1;
            if(MAX_SUPPORTED_VISITS<maximumVisits)maximumVisits=MAX_SUPPORTED_VISITS;
            if(visits>maximumVisits)visits=maximumVisits;
            EnsureScoreUtilityWeights();
            authoritativeGame=game; targetVisits=visits; rootRevision=game.revision; rootGameSettingsRevision=game.settingsRevision; rootSettingsRevision=requestedSettingsRevision; rootOwnerLifecycle=ownerLifecycle;
            simulationState.CopyFromGame(game); CopyStateToRoot(simulationState);
            searchToken++; if(searchToken==0)searchToken=1;
            visitsCompleted=0; treeNodeCount=1; treeEdgeCount=0; bestMove=GoGame.NONE; pendingNode=0; pathDepth=0;
            selectionActive=false;selectionNode=0;
            pendingExpansionStage=0;pendingExpansionCursor=0;pendingExpansionLegalCount=0;
            pendingExpansionNode=-1;pendingExpansionFirstEdge=0;pendingExpansionCount=0;
            pendingExpansionMaximum=0f;pendingExpansionTotal=0f;
            pendingExpansionSubmittedNode=-1;pendingScoreSampleIndex=0;
            pendingScoreWeightTotal=0f;pendingStaticScoreWeighted=0f;
            pendingDynamicScoreWeighted=0f;scoreUtilitySamples=0;
            maxScoreUtilitySamplesOneStep=0;
            rootNeuralScoreMean=0f;rootNeuralScoreStdev=0f;rootNeuralLead=0f;rootNeuralOwnershipMean=0f;rootNeuralOutputRevision=0;
            lastRootCopyMilliseconds=0f;lastNeuralExpansionMilliseconds=0f;
            lastBackupMilliseconds=0f;lastScoreUtilityMilliseconds=0f;
            recentScoreCenter=0f;lastFpuValue=0f;lastExplorationCoefficient=0f;
            fpuSelectionCount=0;dynamicCpuctSelectionCount=0;
            recentScoreCenterUseCount=0;ruleAwareNoResultCount=0;
            searchStepCalls=0;legalMoveChecks=0;simulationPlayCalls=0;
            rootHistoryCopiedEntries=simulationState.lastRootHistoryCopiedEntries;
            rootStateCopiedInts=simulationState.lastRootStateCopiedInts+
                GoGame.AREA*3+10;
            simulationUndoOperations=0;simulationRootResets=0;
            if(referenceTraceEnabled&&referenceTraceNodeIds!=null)
            {
                referenceTraceExpansionCount=0;
                for(int i=0;i<referenceTraceNodeIds.Length;i++)
                {
                    referenceTraceNodeIds[i]=-1;
                    referenceTraceParentNodeIds[i]=-1;
                    referenceTraceParentMoves[i]=GoGame.NONE;
                    referenceTraceExpansionParentNodeIds[i]=-1;
                    referenceTraceExpansionParentMoves[i]=GoGame.NONE;
                }
                referenceTraceParentNodeIds[0]=-1;
                referenceTraceParentMoves[0]=GoGame.NONE;
                referenceDecisionCount=0;
                if(referenceDecisionSimulationIndex!=null)
                    for(int i=0;i<referenceDecisionSimulationIndex.Length;i++)
                    {
                        referenceDecisionSimulationIndex[i]=-1;
                        referenceDecisionParentVisits[i]=0;
                        referenceDecisionSelectedMove[i]=GoGame.NONE;
                        referenceDecisionSelectedEdgeVisits[i]=0;
                        referenceDecisionPolicyMassVisited[i]=0f;
                        referenceDecisionTotalChildWeight[i]=0f;
                        referenceDecisionFpu[i]=0f;
                        referenceDecisionExploration[i]=0f;
                        referenceDecisionBestScore[i]=0f;
                        referenceDecisionSecondBestScore[i]=0f;
                        referenceDecisionSelectedPrior[i]=0f;
                        referenceDecisionSelectedQ[i]=0f;
                        referenceDecisionSelectedU[i]=0f;
                        referenceDecisionSelectedScore[i]=0f;
                    }
                if(referenceDecisionCandidateMoves!=null)
                    for(int i=0;i<referenceDecisionCandidateMoves.Length;i++)
                    {
                        referenceDecisionCandidateMoves[i]=GoGame.NONE;
                        referenceDecisionCandidateVisits[i]=0;
                        referenceDecisionCandidatePrior[i]=0f;
                        referenceDecisionCandidateQ[i]=0f;
                        referenceDecisionCandidateU[i]=0f;
                        referenceDecisionCandidateScore[i]=0f;
                    }
            }
            nodeFirstEdge[0]=0;nodeEdgeCount[0]=0;nodeVisits[0]=0;nodeExpanded[0]=0;nodeTerminal[0]=simulationState.gameState==GoGame.STATE_PLAYING?0:1;nodeValueSum[0]=0f;
            pathNodes[0]=0; pendingPlayer=simulationState.sideToMove; SetPendingHash(); phase=PHASE_WAITING_NEURAL;
            return true;
        }

        public int Step()
        {
            if(phase==PHASE_WAITING_NEURAL||phase==PHASE_COMPLETE||phase==PHASE_CANCELLED)return phase;
            if(phase==PHASE_BACKING_UP){StepBackup(32);return phase;}
            if(phase!=PHASE_RUNNING&&phase!=PHASE_SELECTING)return phase;
            if(authoritativeGame!=null&&authoritativeGame.revision!=rootRevision)
            {
                lastError="authoritative Go position changed during search";CancelSearch();return phase;
            }
            if(visitsCompleted>=targetVisits){Complete();return phase;}
            if(!selectionActive)
            {
                searchStepCalls++;
                ResetSimulationToRoot();pathDepth=0;pathNodes[0]=0;
                selectionNode=0;selectionActive=true;phase=PHASE_SELECTING;
            }
            int work=0;
            while(selectionActive&&work<SELECTION_EDGES_PER_STEP)
            {
                int node=selectionNode;
                if(nodeTerminal[node]!=0)
                {
                    selectionActive=false;
                    Backpropagate(simulationState.TerminalValueForSideToMove());
                    return phase;
                }
                if(nodeExpanded[node]==0)
                {
                    selectionActive=false;
                    lastError="selected an unexpanded node without a pending neural request";phase=PHASE_CANCELLED;return phase;
                }
                int edge=SelectEdge(node);
                if(edge<0)
                {
                    nodeTerminal[node]=1;selectionActive=false;
                    Backpropagate(simulationState.TerminalValueForSideToMove());return phase;
                }
                if(pathDepth>=MAX_PATH_DEPTH-1){selectionActive=false;lastError="MCTS path depth exceeded fixed capacity";phase=PHASE_CANCELLED;return phase;}
                int move=edgeMove[edge];
                simulationPlayCalls++;
                if(!simulationState.Play(move))
                {
                    selectionActive=false;
                    lastError="expanded edge became illegal during selection";phase=PHASE_CANCELLED;return phase;
                }
                work++;
                pathEdges[pathDepth]=edge;pathDepth++;
                int child=edgeChild[edge];
                if(child<0)
                {
                    child=CreateNode(node);
                    if(child<0){selectionActive=false;phase=PHASE_CANCELLED;return phase;}
                    edgeChild[edge]=child;
                    if(referenceTraceEnabled&&referenceTraceParentNodeIds!=null&&
                        child<referenceTraceParentNodeIds.Length)
                    {
                        referenceTraceParentNodeIds[child]=node;
                        referenceTraceParentMoves[child]=move;
                    }
                    pathNodes[pathDepth]=child;pendingNode=child;pendingPlayer=simulationState.sideToMove;SetPendingHash();
                    if(simulationState.gameState!=GoGame.STATE_PLAYING)
                    {
                        nodeTerminal[child]=1;nodeExpanded[child]=1;selectionActive=false;
                        Backpropagate(simulationState.TerminalValueForSideToMove());return phase;
                    }
                    selectionActive=false;
                    phase=PHASE_WAITING_NEURAL;return phase;
                }
                pathNodes[pathDepth]=child;selectionNode=child;
            }
            return phase;
        }

        public bool SubmitNeuralLogits(int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3,float[] policySpatialLogits,float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2)
        {
            return SubmitNeuralLogitsInternal(-1,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatialLogits,policyPassLogit,valueLogit0,valueLogit1,valueLogit2,0f,0f);
        }

        public bool SubmitNeuralLogitsWithScore(int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3,float[] policySpatialLogits,float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2,float scoreMean,float scoreStdev)
        {
            return SubmitNeuralLogitsInternal(-1,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatialLogits,policyPassLogit,valueLogit0,valueLogit1,valueLogit2,scoreMean,scoreStdev);
        }

        public bool SubmitNeuralLogitsForNode(int resultNode,int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3,float[] policySpatialLogits,float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2)
        {
            return SubmitNeuralLogitsInternal(resultNode,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatialLogits,policyPassLogit,valueLogit0,valueLogit1,valueLogit2,0f,0f);
        }

        public bool SubmitNeuralLogitsForNodeWithScore(int resultNode,int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3,float[] policySpatialLogits,float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2,float scoreMean,float scoreStdev)
        {
            return SubmitNeuralLogitsInternal(resultNode,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatialLogits,policyPassLogit,valueLogit0,valueLogit1,valueLogit2,scoreMean,scoreStdev);
        }

        /// <summary>
        /// Starts an exact neural-result expansion without doing the complete
        /// 362-action legality/edge/normalization pass in the caller's frame.
        /// The controller advances it through StepPendingExpansion.
        /// </summary>
        public bool BeginNeuralLogitsForNodeWithScore(int resultNode,int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3,float[] policySpatialLogits,float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2,float scoreMean,float scoreStdev)
        {
            if(!IsCurrentResult(resultNode,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3))return false;
            if(policySpatialLogits==null||policySpatialLogits.Length<GoGame.AREA){lastError="policy logits are too short";phase=PHASE_CANCELLED;return false;}
            if(pendingNode<0||pendingNode>=treeNodeCount){lastError="pending node is invalid";phase=PHASE_CANCELLED;return false;}
            CaptureReferenceExpansion(resultNode,policySpatialLogits,policyPassLogit,
                valueLogit0,valueLogit1,valueLogit2,scoreMean,scoreStdev);
            pendingExpansionNode=pendingNode;
            for(int i=0;i<GoGame.AREA;i++)pendingPolicySpatial[i]=policySpatialLogits[i];
            pendingPolicyPass=policyPassLogit;pendingValue0=valueLogit0;pendingValue1=valueLogit1;pendingValue2=valueLogit2;
            pendingScoreMean=scoreMean;pendingScoreStdev=scoreStdev;
            pendingResultRevision=resultRevision;
            pendingExpansionStage=0;pendingExpansionCursor=0;pendingExpansionLegalCount=0;
            pendingExpansionFirstEdge=treeEdgeCount;pendingExpansionCount=0;
            pendingExpansionMaximum=policyPassLogit;pendingExpansionTotal=0f;
            pendingExpansionSubmittedNode=-1;pendingScoreSampleIndex=0;
            pendingScoreWeightTotal=0f;pendingStaticScoreWeighted=0f;
            pendingDynamicScoreWeighted=0f;
            pendingExpansionStartedAt=Time.realtimeSinceStartup;
            phase=PHASE_EXPANDING;
            return true;
        }

        private void CaptureReferenceExpansion(int resultNode,float[] policySpatialLogits,
            float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2,
            float scoreMean,float scoreStdev)
        {
            if(!referenceTraceEnabled||referenceTraceNodeIds==null||
                referenceTraceExpansionCount>=referenceTraceNodeIds.Length)return;
            int index=referenceTraceExpansionCount++;
            referenceTraceNodeIds[index]=pendingNode;
            referenceTraceExpansionParentNodeIds[index]=pendingNode==0?-1:
                (pendingNode<referenceTraceParentNodeIds.Length?
                    referenceTraceParentNodeIds[pendingNode]:-1);
            referenceTraceExpansionParentMoves[index]=pendingNode==0?GoGame.NONE:
                (pendingNode<referenceTraceParentMoves.Length?
                    referenceTraceParentMoves[pendingNode]:GoGame.NONE);
            int maskBase=index*(GoGame.AREA+1);
            for(int loc=0;loc<GoGame.AREA;loc++)
            {
                referenceTraceLegalMask[maskBase+loc]=
                    (simulationState.GetMoveMask(loc)&1)!=0?1:0;
                referenceTracePolicySpatial[index*GoGame.AREA+loc]=
                    policySpatialLogits[loc];
            }
            referenceTraceLegalMask[maskBase+GoGame.AREA]=1;
            referenceTracePolicyPass[index]=policyPassLogit;
            referenceTraceValue0[index]=valueLogit0;
            referenceTraceValue1[index]=valueLogit1;
            referenceTraceValue2[index]=valueLogit2;
            referenceTraceScoreMean[index]=scoreMean;
            referenceTraceScoreStdev[index]=scoreStdev;
        }

        private bool SubmitNeuralLogitsInternal(int resultNode,int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3,float[] policySpatialLogits,float policyPassLogit,float valueLogit0,float valueLogit1,float valueLogit2,float scoreMean,float scoreStdev)
        {
            if(!BeginNeuralLogitsForNodeWithScore(resultNode,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatialLogits,policyPassLogit,valueLogit0,valueLogit1,valueLogit2,scoreMean,scoreStdev))return false;
            while(phase==PHASE_EXPANDING)StepPendingExpansion(256);
            return phase==PHASE_RUNNING||phase==PHASE_COMPLETE;
        }

        public void CancelSearch()
        {
            searchToken++;if(searchToken==0)searchToken=1;
            pendingNode=-1;pendingExpansionNode=-1;pendingExpansionStage=0;
            selectionActive=false;selectionNode=0;
            pendingExpansionCursor=0;pendingExpansionLegalCount=0;phase=PHASE_CANCELLED;
        }

        public int GetRootVisitsForMove(int move)
        {
            if(treeNodeCount<=0)return 0;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];
            for(int i=0;i<count;i++){int edge=first+i;if(edgeMove[edge]==move)return edgeVisits[edge];}
            return 0;
        }

        public int GetRootCandidateMove(int rank)
        {
            int edge=GetRootCandidateEdge(rank);
            return edge<0?GoGame.NONE:edgeMove[edge];
        }

        public int GetRootCandidateVisits(int rank)
        {
            int edge=GetRootCandidateEdge(rank);
            return edge<0?0:edgeVisits[edge];
        }

        /// <summary>
        /// Raw root edge-visit diagnostics.  KataGo's analysis JSON exposes
        /// edgeVisits separately from its play-selection ordering; these
        /// accessors let a verifier compare the complete tie set without
        /// truncating it to the UI's top-ten list.
        /// </summary>
        public int GetRootVisitArgmaxVisits()
        {
            if(treeNodeCount<=0||nodeEdgeCount==null)return 0;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];int maximum=0;
            for(int i=0;i<count;i++)if(edgeVisits[first+i]>maximum)maximum=edgeVisits[first+i];
            return maximum;
        }

        public int GetRootVisitArgmaxTieCount()
        {
            int maximum=GetRootVisitArgmaxVisits();if(maximum<=0)return 0;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];int ties=0;
            for(int i=0;i<count;i++)if(edgeVisits[first+i]==maximum)ties++;
            return ties;
        }

        public int GetRootVisitArgmaxMove(int rank)
        {
            int maximum=GetRootVisitArgmaxVisits();if(maximum<=0||rank<0)return GoGame.NONE;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];int found=0;
            int previousMove=-1;
            while(found<=rank)
            {
                int bestMove=GoGame.AREA+1;
                for(int i=0;i<count;i++)
                {
                    int edge=first+i;if(edgeVisits[edge]!=maximum)continue;
                    int move=edgeMove[edge];
                    if(move>previousMove&&move<bestMove)bestMove=move;
                }
                if(bestMove>GoGame.AREA)return GoGame.NONE;
                if(found==rank)return bestMove;
                previousMove=bestMove;
                found++;
            }
            return GoGame.NONE;
        }

        public float GetRootCandidatePrior(int rank)
        {
            int edge=GetRootCandidateEdge(rank);
            return edge<0?0f:edgePrior[edge];
        }

        public float GetRootCandidateValue(int rank)
        {
            int edge=GetRootCandidateEdge(rank);
            return edge<0||edgeVisits[edge]<=0?0f:edgeValueSum[edge]/edgeVisits[edge];
        }

        private int GetRootCandidateEdge(int rank)
        {
            // Root candidate telemetry is also consumed by the deterministic
            // search-fidelity benchmark.  It must be able to inspect the full
            // ranked root set, not just the three entries used by the older
            // UI diagnostic.  This is read-only observation; it does not alter
            // root sampling, policyTopK or the committed move.
            if(rank<0||rank>=rootSelectionEdges.Length||treeNodeCount<=0||
                nodeExpanded[0]==0)return -1;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];int found=0;
            for(int pass=0;pass<=rank;pass++)
            {
                int best=-1;
                for(int i=0;i<count;i++)
                {
                    int edge=first+i;if(edgeVisits[edge]<=0)continue;
                    bool used=false;
                    for(int j=0;j<found;j++)if(rootSelectionEdges[j]==edge){used=true;break;}
                    if(used)continue;
                    if(best<0||IsBetterRootEdge(edge,best))best=edge;
                }
                if(best<0)return -1;
                rootSelectionEdges[found++]=best;
            }
            return rootSelectionEdges[rank];
        }

        /// <summary>
        /// Returns the deterministic root ranking winner (visits descending,
        /// prior descending, then location ascending), independently of the
        /// product's temperature-based sampled move.
        /// </summary>
        public int GetDeterministicRootMove()
        { return GetRootCandidateMove(0); }

        public int GetDeterministicRootVisits()
        { return GetRootCandidateVisits(0); }

        /// <summary>
        /// Returns the move with the largest neural prior at the expanded root.
        /// This is a developer/runtime telemetry hook for proving that the
        /// selected move is not being confused with a policy-only baseline.
        /// Search still selects the committed move from backed-up root visits.
        /// </summary>
        public int GetRootPriorMove()
        {
            if(treeNodeCount<=0||nodeExpanded[0]==0)return GoGame.NONE;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];int best=GoGame.NONE;
            float bestPrior=-1f;
            for(int i=0;i<count;i++)
            {
                int edge=first+i;
                if(edgePrior[edge]>bestPrior||
                    (edgePrior[edge]==bestPrior&&edgeMove[edge]<best))
                {
                    bestPrior=edgePrior[edge];best=edgeMove[edge];
                }
            }
            return best;
        }

        public float GetRootMeanValue()
        {return nodeVisits!=null&&nodeValueSum!=null&&treeNodeCount>0&&nodeVisits[0]>0
                ?nodeValueSum[0]/nodeVisits[0]:0f;}

        public int StepPendingExpansion(int workBudget)
        {
            // Compatibility entry point used by synchronous probes and the
            // older editor helpers.  Production search uses the deadline-aware
            // overload below so expansion cannot spend a second hidden frame
            // budget after the controller's admission deadline has expired.
            return StepPendingExpansionUntil(workBudget,-1f);
        }

        public int StepPendingExpansionUntil(int workBudget,float deadline)
        {
            if(phase!=PHASE_EXPANDING)return phase;
            int budget=Mathf.Clamp(workBudget,1,256);
            int initialBudget=budget;
            while(budget>0&&phase==PHASE_EXPANDING)
            {
                if(deadline>0f&&Time.realtimeSinceStartup>=deadline&&budget<initialBudget)
                    break;
                if(pendingExpansionStage==0)
                {
                    if(pendingExpansionCursor>=GoGame.AREA)
                    {
                        pendingExpansionStage=1;pendingExpansionCursor=0;
                        pendingExpansionFirstEdge=treeEdgeCount;
                        pendingExpansionCount=0;pendingExpansionTotal=0f;
                        continue;
                    }
                    int loc=pendingExpansionCursor++;
                    legalMoveChecks++;
                    if((simulationState.GetMoveMask(loc)&1)!=0)
                    {
                        legalMoves[pendingExpansionLegalCount++]=loc;
                        if(pendingPolicySpatial[loc]>pendingExpansionMaximum)
                            pendingExpansionMaximum=pendingPolicySpatial[loc];
                    }
                    budget--;
                    continue;
                }

                if(pendingExpansionStage==1)
                {
                    if(pendingExpansionCursor<pendingExpansionLegalCount)
                    {
                        int loc=legalMoves[pendingExpansionCursor++];
                        float weight=Mathf.Exp(Mathf.Clamp(pendingPolicySpatial[loc]-pendingExpansionMaximum,-80f,80f));
                        if(!AddEdge(pendingExpansionNode,loc,weight))return phase;
                        pendingExpansionTotal+=weight;pendingExpansionCount++;budget--;
                        continue;
                    }
                    float passWeight=Mathf.Exp(Mathf.Clamp(pendingPolicyPass-pendingExpansionMaximum,-80f,80f));
                    if(!AddEdge(pendingExpansionNode,GoGame.PASS,passWeight))return phase;
                    pendingExpansionTotal+=passWeight;pendingExpansionCount++;
                    pendingExpansionStage=2;pendingExpansionCursor=0;
                    continue;
                }

                if(pendingExpansionStage==2)
                {
                    if(pendingExpansionCursor<pendingExpansionCount)
                    {
                        int edge=pendingExpansionFirstEdge+pendingExpansionCursor++;
                        edgePrior[edge]/=pendingExpansionTotal;
                        budget--;
                        continue;
                    }
                    if(pendingExpansionCount<=0||pendingExpansionTotal<=0f)
                    {
                        lastError="neural expansion produced no actions";
                        phase=PHASE_CANCELLED;return phase;
                    }
                    pendingExpansionSubmittedNode=pendingExpansionNode;
                    nodeFirstEdge[pendingExpansionSubmittedNode]=pendingExpansionFirstEdge;
                    nodeEdgeCount[pendingExpansionSubmittedNode]=pendingExpansionCount;
                    nodeExpanded[pendingExpansionSubmittedNode]=1;
                    nodeTerminal[pendingExpansionSubmittedNode]=0;
                    pendingNode=-1;pendingExpansionNode=-1;pendingExpansionStage=3;
                    bool suppressNoResult=(searchSemanticsMode&SEMANTICS_KATAGO_RULE_AWARE_NO_RESULT)!=0&&
                        simulationState.positionalSuperko&&simulationState.areaScoring;
                    float noResultProbability=NoResultProbability(pendingValue0,pendingValue1,pendingValue2,
                        suppressNoResult);
                    if(suppressNoResult)ruleAwareNoResultCount++;
                    pendingEffectiveScoreMean=pendingScoreMean*(1f-noResultProbability);
                    pendingEffectiveScoreStdev=EffectiveScoreStdev(pendingScoreMean,pendingScoreStdev,noResultProbability);
                    if(pendingExpansionSubmittedNode==0)
                    {
                        if((searchSemanticsMode&SEMANTICS_KATAGO_RECENT_SCORE_CENTER)!=0)
                            recentScoreCenter=pendingEffectiveScoreMean;
                        rootNeuralScoreMean=pendingEffectiveScoreMean;
                        rootNeuralScoreStdev=pendingEffectiveScoreStdev;
                        rootNeuralOutputRevision=pendingResultRevision;
                    }
                    pendingScoreSampleIndex=0;pendingScoreWeightTotal=0f;
                    pendingStaticScoreWeighted=0f;pendingDynamicScoreWeighted=0f;
                    return phase;
                }

                if(pendingExpansionStage==3)
                {
                    float scoreStartedAt=Time.realtimeSinceStartup;
                    int completed=0;
                    // Use the remaining cooperative work budget rather than a
                    // fixed eight-sample frame barrier. The 101-point
                    // quadrature remains exact and in the same order; a fast
                    // frame may consume all of it, while a real deadline can
                    // still stop the loop before the VM budget is exhausted.
                    while(pendingScoreSampleIndex<=100&&completed<budget)
                    {
                        if(deadline>0f&&completed>0&&
                            Time.realtimeSinceStartup>=deadline)break;
                        float z=(pendingScoreSampleIndex-50)*0.1f;
                        float weight=GetScoreUtilityWeight(pendingScoreSampleIndex);
                        float score=pendingEffectiveScoreMean+pendingEffectiveScoreStdev*z;
                        float dynamicScore=score;
                        if((searchSemanticsMode&SEMANTICS_KATAGO_RECENT_SCORE_CENTER)!=0)
                        {
                            dynamicScore-=recentScoreCenter;
                            recentScoreCenterUseCount++;
                        }
                        pendingStaticScoreWeighted+=weight*Mathf.Atan(score/38f)*0.63661975f;
                        pendingDynamicScoreWeighted+=weight*Mathf.Atan(dynamicScore/14.25f)*0.63661975f;
                        pendingScoreWeightTotal+=weight;
                        pendingScoreSampleIndex++;completed++;scoreUtilitySamples++;budget--;
                    }
                    if(completed>maxScoreUtilitySamplesOneStep)
                        maxScoreUtilitySamplesOneStep=completed;
                    lastScoreUtilityMilliseconds+=
                        (Time.realtimeSinceStartup-scoreStartedAt)*1000f;
                    if(pendingScoreSampleIndex<=100)return phase;
                    pendingExpansionStage=4;
                    continue;
                }

                if(pendingExpansionStage==4)
                {
                    float utility=0f;
                    if(pendingScoreWeightTotal>0f)
                        utility=(pendingStaticScoreWeighted/pendingScoreWeightTotal)*0.10f+
                            (pendingDynamicScoreWeighted/pendingScoreWeightTotal)*0.30f;
                    if(utility>0.40f)utility=0.40f;
                    if(utility<-0.40f)utility=-0.40f;
                    bool suppressValueNoResult=(searchSemanticsMode&SEMANTICS_KATAGO_RULE_AWARE_NO_RESULT)!=0&&
                        simulationState.positionalSuperko&&simulationState.areaScoring;
                    float value=ValueFromLogits(pendingValue0,pendingValue1,pendingValue2,
                        suppressValueNoResult)+utility;
                    lastNeuralExpansionMilliseconds+=(Time.realtimeSinceStartup-pendingExpansionStartedAt)*1000f;
                    Backpropagate(value);
                    return phase;
                }

                lastError="MCTS expansion entered an unknown stage";
                phase=PHASE_CANCELLED;
                return phase;
            }
            return phase;
        }

        private int SelectEdge(int node)
        {
            int first=nodeFirstEdge[node];int count=nodeEdgeCount[node];if(count<=0)return -1;
            float totalChildWeight=0f;float policyMassVisited=0f;
            for(int i=0;i<count;i++)
            {
                int edge=first+i;
                totalChildWeight+=edgeVisits[edge];
                if(edgeVisits[edge]>0)policyMassVisited+=edgePrior[edge];
            }
            float fpuValue=GetFpuValue(node,policyMassVisited);
            float exploreScaling=GetExploreScaling(node,totalChildWeight);
            lastFpuValue=fpuValue;lastExplorationCoefficient=exploreScaling;
            float bestScore=-3.402823e+38f;float secondBestScore=-3.402823e+38f;float bestPrior=-1f;int bestMoveValue=GoGame.AREA+1;int best=-1;
            for(int i=0;i<count;i++)
            {
                int edge=first+i;
                float q=edgeVisits[edge]>0?edgeValueSum[edge]/edgeVisits[edge]:
                    ((searchSemanticsMode&SEMANTICS_KATAGO_FPU)!=0?fpuValue:0f);
                float u=exploreScaling*edgePrior[edge]/(1f+edgeVisits[edge]);
                float score=q+u;
                bool better=score>bestScore||(score==bestScore&&(edgePrior[edge]>bestPrior||(edgePrior[edge]==bestPrior&&edgeMove[edge]<bestMoveValue)));
                if(better)
                {
                    secondBestScore=bestScore;
                    bestScore=score;bestPrior=edgePrior[edge];bestMoveValue=edgeMove[edge];best=edge;
                }
                else if(score>secondBestScore)secondBestScore=score;
            }
            if(node==0&&referenceTraceEnabled&&best>=0)
                CaptureReferenceDecision(node,totalChildWeight,policyMassVisited,fpuValue,
                    exploreScaling,best,bestScore,secondBestScore,first,count);
            return best;
        }

        private void CaptureReferenceDecision(int node,float totalChildWeight,
            float policyMassVisited,float fpuValue,float exploreScaling,int selected,
            float bestScore,float secondBestScore,int first,int count)
        {
            if(referenceDecisionSimulationIndex==null||
                referenceDecisionCount>=referenceDecisionSimulationIndex.Length)return;
            int index=referenceDecisionCount++;
            referenceDecisionSimulationIndex[index]=visitsCompleted;
            referenceDecisionParentVisits[index]=nodeVisits[node];
            referenceDecisionPolicyMassVisited[index]=policyMassVisited;
            referenceDecisionTotalChildWeight[index]=totalChildWeight;
            referenceDecisionFpu[index]=fpuValue;
            referenceDecisionExploration[index]=exploreScaling;
            referenceDecisionBestScore[index]=bestScore;
            referenceDecisionSecondBestScore[index]=secondBestScore;
            referenceDecisionSelectedMove[index]=edgeMove[selected];
            referenceDecisionSelectedEdgeVisits[index]=edgeVisits[selected];
            referenceDecisionSelectedPrior[index]=edgePrior[selected];
            referenceDecisionSelectedQ[index]=edgeVisits[selected]>0?
                edgeValueSum[selected]/edgeVisits[selected]:fpuValue;
            referenceDecisionSelectedU[index]=exploreScaling*edgePrior[selected]/
                (1f+edgeVisits[selected]);
            referenceDecisionSelectedScore[index]=referenceDecisionSelectedQ[index]+
                referenceDecisionSelectedU[index];
            for(int i=0;i<REFERENCE_DECISION_CANDIDATE_COUNT;i++)
            {referenceDecisionScratchEdges[i]=-1;referenceDecisionScratchScores[i]=-3.402823e+38f;}
            for(int i=0;i<count;i++)
            {
                int edge=first+i;
                float q=edgeVisits[edge]>0?edgeValueSum[edge]/edgeVisits[edge]:fpuValue;
                float u=exploreScaling*edgePrior[edge]/(1f+edgeVisits[edge]);
                float score=q+u;
                int insert=REFERENCE_DECISION_CANDIDATE_COUNT;
                for(int j=0;j<REFERENCE_DECISION_CANDIDATE_COUNT;j++)
                {
                    int existing=referenceDecisionScratchEdges[j];
                    if(existing<0||score>referenceDecisionScratchScores[j]||
                        (score==referenceDecisionScratchScores[j]&&
                        (edgePrior[edge]>edgePrior[existing]||
                        (edgePrior[edge]==edgePrior[existing]&&edgeMove[edge]<edgeMove[existing]))))
                    {insert=j;break;}
                }
                if(insert>=REFERENCE_DECISION_CANDIDATE_COUNT)continue;
                for(int j=REFERENCE_DECISION_CANDIDATE_COUNT-1;j>insert;j--)
                {referenceDecisionScratchEdges[j]=referenceDecisionScratchEdges[j-1];referenceDecisionScratchScores[j]=referenceDecisionScratchScores[j-1];}
                referenceDecisionScratchEdges[insert]=edge;referenceDecisionScratchScores[insert]=score;
            }
            int destinationBase=index*REFERENCE_DECISION_CANDIDATE_COUNT;
            for(int i=0;i<REFERENCE_DECISION_CANDIDATE_COUNT;i++)
            {
                int edge=referenceDecisionScratchEdges[i];
                if(edge<0)continue;
                int dst=destinationBase+i;
                referenceDecisionCandidateMoves[dst]=edgeMove[edge];
                referenceDecisionCandidateVisits[dst]=edgeVisits[edge];
                referenceDecisionCandidatePrior[dst]=edgePrior[edge];
                referenceDecisionCandidateQ[dst]=edgeVisits[edge]>0?edgeValueSum[edge]/edgeVisits[edge]:fpuValue;
                referenceDecisionCandidateU[dst]=exploreScaling*edgePrior[edge]/(1f+edgeVisits[edge]);
                referenceDecisionCandidateScore[dst]=referenceDecisionCandidateQ[dst]+referenceDecisionCandidateU[dst];
            }
        }

        private float GetExploreScaling(int node,float totalChildWeight)
        {
            if((searchSemanticsMode&SEMANTICS_KATAGO_DYNAMIC_CPUCT)==0)
                return explorationConstant*Mathf.Sqrt(
                    (nodeVisits[node]>=0?nodeVisits[node]:0)+1f);
            dynamicCpuctSelectionCount++;
            // KataGo's shipped deterministic defaults are cpuctLog=0,
            // cpuctBase=500 and stdevScale=0.  Keep those constants explicit;
            // the mode changes only the mathematically distinct total-child
            // weighting and can be A/B tested without tuning product cpuct.
            float cpuct=explorationConstant+KATAGO_CPUCT_LOG*
                Mathf.Log((totalChildWeight+KATAGO_CPUCT_BASE)/KATAGO_CPUCT_BASE);
            return cpuct*Mathf.Sqrt(totalChildWeight+0.01f);
        }

        private float GetFpuValue(int node,float policyMassVisited)
        {
            if((searchSemanticsMode&SEMANTICS_KATAGO_FPU)==0)return 0f;
            float parentUtility=nodeVisits[node]>0?
                nodeValueSum[node]/nodeVisits[node]:0f;
            // The bundled KataGo defaults use fpuReductionMax=.2 and
            // fpuLossProp=0. In Udon's player-to-move convention the FPU
            // reduction is always subtracted, independent of absolute color.
            float reduction=KATAGO_FPU_REDUCTION_MAX*Mathf.Sqrt(policyMassVisited);
            fpuSelectionCount++;
            return parentUtility-reduction;
        }

        private void Backpropagate(float value)
        {
            if(value>1f)value=1f;if(value<-1f)value=-1f;
            backupValue=value;backupDepth=pathDepth;
            backupStartedAt=Time.realtimeSinceStartup;
            phase=PHASE_BACKING_UP;
        }

        private void StepBackup(int workBudget)
        {
            int budget=Mathf.Clamp(workBudget,1,64);
            while(backupDepth>=0&&budget>0)
            {
                int node=pathNodes[backupDepth];
                nodeVisits[node]++;nodeValueSum[node]+=backupValue;
                if(backupDepth>0)
                {
                    int edge=pathEdges[backupDepth-1];
                    edgeVisits[edge]++;edgeValueSum[edge]+=-backupValue;
                }
                backupValue=-backupValue;backupDepth--;budget--;
            }
            if(backupDepth>=0)return;
            visitsCompleted++;if(visitsCompleted>=targetVisits)Complete();else phase=PHASE_RUNNING;
            lastBackupMilliseconds+=(Time.realtimeSinceStartup-backupStartedAt)*1000f;
        }

        private void Complete()
        {
            selectionActive=false;phase=PHASE_COMPLETE;bestMove=ChooseRootMove();pendingNode=-1;
        }

        private int ChooseRootMove()
        {
            if(treeNodeCount<=0)return GoGame.NONE;
            int first=nodeFirstEdge[0];int count=nodeEdgeCount[0];
            int positiveCount=0;
            for(int i=0;i<count;i++)if(edgeVisits[first+i]>0)positiveCount++;
            if(positiveCount<=0)
            {
                int fallback=first;for(int i=1;i<count;i++)if(IsBetterRootEdge(first+i,fallback))fallback=first+i;
                return edgeMove[fallback];
            }
            int limit=rootPolicyTopK;if(limit<1)limit=1;if(limit>positiveCount)limit=positiveCount;
            int selectedCount=0;
            for(int i=0;i<count;i++)
            {
                int edge=first+i;
                if(edgeVisits[edge]<=0)continue;
                if(selectedCount<limit)
                {
                    rootSelectionEdges[selectedCount++]=edge;
                    SortRootSelectionTail(selectedCount-1);
                }
                else if(IsBetterRootEdge(edge,rootSelectionEdges[selectedCount-1]))
                {
                    rootSelectionEdges[selectedCount-1]=edge;
                    SortRootSelectionTail(selectedCount-1);
                }
            }
            if(selectedCount<=0)return GoGame.NONE;
            if(rootMoveTemperature<=0.0101f)return edgeMove[rootSelectionEdges[0]];
            float maximum=-3.402823e+38f;
            for(int i=0;i<selectedCount;i++)
            {
                int edge=rootSelectionEdges[i];
                float logWeight=Mathf.Log(edgeVisits[edge])/rootMoveTemperature;
                rootSelectionWeights[i]=logWeight; if(logWeight>maximum)maximum=logWeight;
            }
            float total=0f;
            for(int i=0;i<selectedCount;i++)
            {
                float weight=Mathf.Exp(Mathf.Clamp(rootSelectionWeights[i]-maximum,-80f,80f));
                rootSelectionWeights[i]=weight;total+=weight;
            }
            if(total<=0f)return edgeMove[rootSelectionEdges[0]];
            float sample=NextRootSelectionRandom()*total;
            for(int i=0;i<selectedCount;i++)
            {
                sample-=rootSelectionWeights[i];
                if(sample<=0f)return edgeMove[rootSelectionEdges[i]];
            }
            return edgeMove[rootSelectionEdges[selectedCount-1]];
        }

        private bool IsBetterRootEdge(int left,int right)
        {
            if(edgeVisits[left]!=edgeVisits[right])return edgeVisits[left]>edgeVisits[right];
            if(edgePrior[left]!=edgePrior[right])return edgePrior[left]>edgePrior[right];
            return edgeMove[left]<edgeMove[right];
        }

        private void SortRootSelectionTail(int index)
        {
            while(index>0&&IsBetterRootEdge(rootSelectionEdges[index],rootSelectionEdges[index-1]))
            {
                int swap=rootSelectionEdges[index-1];rootSelectionEdges[index-1]=rootSelectionEdges[index];rootSelectionEdges[index]=swap;index--;
            }
        }

        private float NextRootSelectionRandom()
        {
            rootSelectionSeed=rootSelectionSeed*1103515245+12345;
            int positive=rootSelectionSeed&0x7fffffff;
            return positive/2147483648f;
        }

        private int CreateNode(int parent)
        {
            if(!nodeStorageAllocated&& !EnsureNodeStorage())
            {
                lastError="MCTS node storage is unavailable";return -1;
            }
            if(treeNodeCount>=MAX_NODES){lastError="MCTS node capacity exhausted";return -1;}
            int node=treeNodeCount++;nodeFirstEdge[node]=0;nodeEdgeCount[node]=0;nodeVisits[node]=0;nodeExpanded[node]=0;nodeTerminal[node]=0;nodeValueSum[node]=0f;return node;
        }

        private bool AddEdge(int parent,int move,float weight)
        {
            if(treeEdgeCount>=MAX_EDGES){lastError="MCTS edge capacity exhausted";phase=PHASE_CANCELLED;return false;}
            // Edges are stored contiguously per node, so nodeFirstEdge and
            // nodeEdgeCount already identify the parent. Do not retain a
            // redundant parent array for every edge.
            int edge=treeEdgeCount++;edgeChild[edge]=-1;edgeMove[edge]=move;edgeVisits[edge]=0;edgePrior[edge]=weight;edgeValueSum[edge]=0f;return true;
        }

        private float ValueFromLogits(float win,float loss,float noResult,bool suppressNoResult)
        {
            // KataGo removes the no-result logit before softmax for rules
            // where a no-result outcome is impossible.  The maximum must be
            // computed from the surviving logits as well; retaining a huge
            // suppressed logit as the numerical anchor would underflow both
            // win/loss terms and silently turn the value into zero.
            float maximum=suppressNoResult?Mathf.Max(win,loss):Mathf.Max(win,Mathf.Max(loss,noResult));
            float w=Mathf.Exp(Mathf.Clamp(win-maximum,-80f,80f));float l=Mathf.Exp(Mathf.Clamp(loss-maximum,-80f,80f));float n=suppressNoResult?0f:Mathf.Exp(Mathf.Clamp(noResult-maximum,-80f,80f));float total=w+l+n;return total>0f?(w-l)/total:0f;
        }

        private float NoResultProbability(float win,float loss,float noResult,bool suppressNoResult)
        {
            float maximum=suppressNoResult?Mathf.Max(win,loss):Mathf.Max(win,Mathf.Max(loss,noResult));
            float w=Mathf.Exp(Mathf.Clamp(win-maximum,-80f,80f));
            float l=Mathf.Exp(Mathf.Clamp(loss-maximum,-80f,80f));
            float n=suppressNoResult?0f:Mathf.Exp(Mathf.Clamp(noResult-maximum,-80f,80f));
            float total=w+l+n;
            return total>0f?n/total:0.33333334f;
        }

        private float EffectiveScoreStdev(float conditionalMean,float conditionalStdev,float noResultProbability)
        {
            float resultWeight=1f-Mathf.Clamp01(noResultProbability);
            float effectiveMean=conditionalMean*resultWeight;
            float secondMoment=(conditionalMean*conditionalMean+conditionalStdev*conditionalStdev)*resultWeight;
            float variance=secondMoment-effectiveMean*effectiveMean;
            return Mathf.Sqrt(Mathf.Max(0f,variance));
        }

        private float ScoreUtility(float scoreMean,float scoreStdev)
        {
            // KataGo V8 combines expected score utility at static scale 2 and
            // dynamic scale 0.75. The current fixed Udon tree is current-player
            // relative, so its dynamic center is zero. Use the same bounded
            // normal expectation shape as KataGo's score-value table rather
            // than dropping score stdev. The 101 samples are fixed and contain
            // no allocation, which keeps this path deterministic in Udon.
            float staticUtility=ExpectedScoreUtility(scoreMean,scoreStdev,2f,0f);
            float dynamicCenter=(searchSemanticsMode&SEMANTICS_KATAGO_RECENT_SCORE_CENTER)!=0?
                recentScoreCenter:0f;
            float dynamicUtility=ExpectedScoreUtility(scoreMean,scoreStdev,0.75f,
                dynamicCenter);
            float utility=staticUtility*0.10f+dynamicUtility*0.30f;
            if(utility>0.40f)utility=0.40f;if(utility<-0.40f)utility=-0.40f;
            return utility;
        }

        private float ExpectedScoreUtility(float scoreMean,float scoreStdev,float scale,
            float center)
        {
            float denominator=scale*19f;
            if(denominator<=0f)return 0f;
            EnsureScoreUtilityWeights();
            float total=0f;float weighted=0f;
            for(int i=-50;i<=50;i++)
            {
                float z=i*0.1f;
                float weight=scoreUtilityWeights[i+50];
                float score=scoreMean+scoreStdev*z-center;
                weighted+=weight*Mathf.Atan(score/denominator)*0.63661975f;
                total+=weight;
            }
            return total>0f?weighted/total:0f;
        }

        private void EnsureScoreUtilityWeights()
        {
            if(scoreUtilityWeights==null||scoreUtilityWeights.Length<101)
            {
                scoreUtilityWeights=new float[101];
                scoreUtilityWeightsReady=false;
            }
            if(scoreUtilityWeightsReady)return;
            for(int i=0;i<=100;i++)
            {
                float z=(i-50)*0.1f;
                scoreUtilityWeights[i]=Mathf.Exp(-0.5f*z*z);
            }
            scoreUtilityWeightsReady=true;
        }

        private float GetScoreUtilityWeight(int sampleIndex)
        {
            EnsureScoreUtilityWeights();
            return sampleIndex>=0&&sampleIndex<101?scoreUtilityWeights[sampleIndex]:0f;
        }

        private bool IsCurrentResult(int resultNode,int resultToken,int resultRevision,int resultSettingsRevision,int resultOwnerLifecycle,int resultHash0,int resultHash1,int resultHash2,int resultHash3)
        {
            if(phase!=PHASE_WAITING_NEURAL||(resultNode>=0&&resultNode!=pendingNode)||resultToken!=searchToken||resultRevision!=rootRevision||resultSettingsRevision!=rootSettingsRevision||resultOwnerLifecycle!=rootOwnerLifecycle||resultHash0!=pendingHash0||resultHash1!=pendingHash1||resultHash2!=pendingHash2||resultHash3!=pendingHash3)
            {staleResultsDiscarded++;return false;}
            return true;
        }

        private void SetPendingHash(){pendingHash0=simulationState.currentHash0;pendingHash1=simulationState.currentHash1;pendingHash2=simulationState.currentHash2;pendingHash3=simulationState.currentHash3;}

        private void CopyStateToRoot(GoSearchState source)
        {
            Copy(source.board,rootBoard);Copy(source.previousBoard1,rootPreviousBoard1);Copy(source.previousBoard2,rootPreviousBoard2);for(int i=0;i<5;i++){rootRecentMoveLoc[i]=source.recentMoveLoc[i];rootRecentMovePla[i]=source.recentMovePla[i];}
            // The simulation owns the immutable root history prefix. Only the
            // board/history-plane snapshot and scalar reset state need a
            // second root copy; no duplicate 2049-entry history arrays exist.
            rootHistoryStoneCountVersion=source.historyStoneCountVersion;rootPositionHistoryCount=source.positionHistoryCount;rootSideToMove=source.sideToMove;rootMoveCount=source.moveCount;rootConsecutivePasses=source.consecutivePasses;rootBlackCaptures=source.blackCaptures;rootWhiteCaptures=source.whiteCaptures;rootGameState=source.gameState;rootWinner=source.winner;rootLastMove=source.lastMove;rootKoLoc=source.koLoc;rootHandicapStones=source.handicapStones;rootKomiTimes2=source.komiTimes2;rootPositionalSuperko=source.positionalSuperko;rootAreaScoring=source.areaScoring;rootMultiStoneSuicideLegal=source.multiStoneSuicideLegal;
            rootCurrentHash0=source.currentHash0;rootCurrentHash1=source.currentHash1;rootCurrentHash2=source.currentHash2;rootCurrentHash3=source.currentHash3;rootCurrentStoneCount=source.currentStoneCount;
        }

        private void ResetSimulationToRoot()
        {
            float startedAt=Time.realtimeSinceStartup;
            int appended=simulationState.positionHistoryCount-rootPositionHistoryCount;
            if(appended>0)simulationUndoOperations+=appended;
            simulationRootResets++;
            Copy(rootBoard,simulationState.board);Copy(rootPreviousBoard1,simulationState.previousBoard1);Copy(rootPreviousBoard2,simulationState.previousBoard2);for(int i=0;i<5;i++){simulationState.recentMoveLoc[i]=rootRecentMoveLoc[i];simulationState.recentMovePla[i]=rootRecentMovePla[i];}
            // Play/Pass append history only at positionHistoryCount; they do
            // not mutate the root prefix. Restore the count and root scalar
            // state, then let the next path overwrite its own appended tail.
            // The complete root history was copied once in BeginSearch, so
            // avoid copying five 2049-entry arrays on every visit.
            simulationState.RestoreHistoryBucketsToCount(rootPositionHistoryCount);
            simulationState.historyStoneCountVersion=rootHistoryStoneCountVersion;
            simulationState.sideToMove=rootSideToMove;simulationState.moveCount=rootMoveCount;simulationState.consecutivePasses=rootConsecutivePasses;simulationState.blackCaptures=rootBlackCaptures;simulationState.whiteCaptures=rootWhiteCaptures;simulationState.gameState=rootGameState;simulationState.winner=rootWinner;simulationState.lastMove=rootLastMove;simulationState.koLoc=rootKoLoc;simulationState.handicapStones=rootHandicapStones;simulationState.komiTimes2=rootKomiTimes2;simulationState.positionalSuperko=rootPositionalSuperko;simulationState.areaScoring=rootAreaScoring;simulationState.multiStoneSuicideLegal=rootMultiStoneSuicideLegal;simulationState.currentHash0=rootCurrentHash0;simulationState.currentHash1=rootCurrentHash1;simulationState.currentHash2=rootCurrentHash2;simulationState.currentHash3=rootCurrentHash3;simulationState.currentStoneCount=rootCurrentStoneCount;
            simulationState.InvalidateMoveMaskCache();
            lastRootCopyMilliseconds+=(Time.realtimeSinceStartup-startedAt)*1000f;
        }

        private void Copy(int[] source,int[] target){for(int i=0;i<source.Length&&i<target.Length;i++)target[i]=source[i];}

        private bool EnsureEdgeStorage()
        {
            if(edgeChild!=null&&edgeMove!=null&&edgeVisits!=null&&edgePrior!=null&&edgeValueSum!=null&&
                edgeChild.Length>=MAX_EDGES&&edgeMove.Length>=MAX_EDGES&&edgeVisits.Length>=MAX_EDGES&&
                edgePrior.Length>=MAX_EDGES&&edgeValueSum.Length>=MAX_EDGES)
            {
                edgeStorageAllocated=true;return true;
            }
            edgeChild=new int[MAX_EDGES];edgeMove=new int[MAX_EDGES];edgeVisits=new int[MAX_EDGES];
            edgePrior=new float[MAX_EDGES];edgeValueSum=new float[MAX_EDGES];
            edgeStorageAllocated=edgeChild!=null&&edgeMove!=null&&edgeVisits!=null&&
                edgePrior!=null&&edgeValueSum!=null;
            return edgeStorageAllocated;
        }

        private bool EnsureNodeStorage()
        {
            if(nodeFirstEdge!=null&&nodeEdgeCount!=null&&nodeVisits!=null&&
                nodeExpanded!=null&&nodeTerminal!=null&&nodeValueSum!=null&&
                nodeFirstEdge.Length>=MAX_NODES&&nodeEdgeCount.Length>=MAX_NODES&&
                nodeVisits.Length>=MAX_NODES&&nodeExpanded.Length>=MAX_NODES&&
                nodeTerminal.Length>=MAX_NODES&&nodeValueSum.Length>=MAX_NODES)
            {
                nodeStorageAllocated=true;return true;
            }
            nodeFirstEdge=new int[MAX_NODES];nodeEdgeCount=new int[MAX_NODES];
            nodeVisits=new int[MAX_NODES];nodeExpanded=new int[MAX_NODES];
            nodeTerminal=new int[MAX_NODES];nodeValueSum=new float[MAX_NODES];
            nodeStorageAllocated=nodeFirstEdge!=null&&nodeEdgeCount!=null&&
                nodeVisits!=null&&nodeExpanded!=null&&nodeTerminal!=null&&
                nodeValueSum!=null;
            return nodeStorageAllocated;
        }

        public void ReleaseHeavyResources()
        {
            if(phase!=PHASE_IDLE&&phase!=PHASE_CANCELLED)CancelSearch();
            edgeChild=null;edgeMove=null;edgeVisits=null;
            edgePrior=null;edgeValueSum=null;edgeStorageAllocated=false;
            nodeFirstEdge=null;nodeEdgeCount=null;nodeVisits=null;
            nodeExpanded=null;nodeTerminal=null;nodeValueSum=null;
            nodeStorageAllocated=false;
            treeNodeCount=0;treeEdgeCount=0;
        }

        public int GetEstimatedAllocatedArrayBytes()
        {
            int bytes=0;
            if(nodeFirstEdge!=null)bytes+=nodeFirstEdge.Length*4;
            if(nodeEdgeCount!=null)bytes+=nodeEdgeCount.Length*4;
            if(nodeVisits!=null)bytes+=nodeVisits.Length*4;
            if(nodeExpanded!=null)bytes+=nodeExpanded.Length*4;
            if(nodeTerminal!=null)bytes+=nodeTerminal.Length*4;
            if(nodeValueSum!=null)bytes+=nodeValueSum.Length*4;
            bytes+=(rootBoard.Length+rootPreviousBoard1.Length+
                rootPreviousBoard2.Length+rootRecentMoveLoc.Length+
                rootRecentMovePla.Length+pathNodes.Length+pathEdges.Length+
                rootSelectionEdges.Length+legalMoves.Length)*4;
            bytes+=(rootSelectionWeights.Length+pendingPolicySpatial.Length)*4;
            if(edgeChild!=null)bytes+=edgeChild.Length*4;
            if(edgeMove!=null)bytes+=edgeMove.Length*4;
            if(edgeVisits!=null)bytes+=edgeVisits.Length*4;
            if(edgePrior!=null)bytes+=edgePrior.Length*4;
            if(edgeValueSum!=null)bytes+=edgeValueSum.Length*4;
            return bytes;
        }
    }
}
