using UdonSharp;
using TMPro;
using UnityEngine;
using VRC.Udon.Common;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Xiangqi-family manual-sync telemetry for one Go table. Search data is
    /// published by the authoritative GoAiController and remains separate
    /// from the large synchronized Go position. Remote players can therefore
    /// see the owner's real GPU/PUCT state without synchronizing the tree.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class GoTelemetry : UdonSharpBehaviour
    {
        [UdonSynced] public int revision;
        [UdonSynced] public int controllerState;
        [UdonSynced] public int searchPhase;
        [UdonSynced] public int targetVisits;
        [UdonSynced] public int completedVisits;
        [UdonSynced] public int treeNodes;
        [UdonSynced] public int treeEdges;
        [UdonSynced] public int staleResultsDiscarded;
        [UdonSynced] public int outputRevision;
        [UdonSynced] public int lastSearchFrames;
        [UdonSynced] public int schedulerStride = 1;
        [UdonSynced] public int activeSearches = 1;
        [UdonSynced] public int dispatchesPerFrame = 1;
        [UdonSynced] public float observedFps;
        [UdonSynced] public float searchMilliseconds;
        [UdonSynced] public float progress;
        [UdonSynced] public float scoreMean;
        [UdonSynced] public float scoreStdev;
        [UdonSynced] public float lead;
        [UdonSynced] public float ownershipMean;
        [UdonSynced] public float blackWinProbability;
        [UdonSynced] public float whiteWinProbability;
        [UdonSynced] public float noResultProbability;
        [UdonSynced] public float value;
        [UdonSynced] public float rootQ;
        [UdonSynced] public string statusText = "AI ready";
        [UdonSynced] public string bestMove = "—";
        [UdonSynced] public string rootPriorMove = "—";
        [UdonSynced] public string candidateMove0 = "—";
        [UdonSynced] public string candidateMove1 = "—";
        [UdonSynced] public string candidateMove2 = "—";
        [UdonSynced] public int candidateVisits0;
        [UdonSynced] public int candidateVisits1;
        [UdonSynced] public int candidateVisits2;
        [UdonSynced] public float candidatePrior0;
        [UdonSynced] public float candidatePrior1;
        [UdonSynced] public float candidatePrior2;
        [UdonSynced] public float candidateValue0;
        [UdonSynced] public float candidateValue1;
        [UdonSynced] public float candidateValue2;
        [UdonSynced] public string computeOwner = "—";
        [UdonSynced] public string errorText = "";
        // The last completed root analysis is a separate synchronized
        // snapshot.  It survives the following AI move (which invalidates
        // the in-flight search identity) so every client can keep seeing the
        // same final estimate until a new analysis replaces it.
        [UdonSynced] public bool hasFinalAnalysis;
        [UdonSynced] public int finalAnalysisRevision;
        [UdonSynced] public int finalAnalysisTargetVisits;
        [UdonSynced] public int finalAnalysisCompletedVisits;
        [UdonSynced] public string finalAnalysisStatusText = "No completed AI analysis";
        [UdonSynced] public string finalAnalysisBestMove = "—";
        // Packed display text keeps the synchronized final snapshot compact;
        // it contains the same candidate/probability/score/ownership values
        // that were visible during the live search.
        [UdonSynced] public string finalAnalysisSnapshot = "";
        // Independent identity for the ownership-head presentation pass.  A
        // root score can be ready before all 361 ownership values are decoded.
        [UdonSynced] public int ownershipOutputRevision;

        public GoGame game;
        public GoDifficultyProfile difficulty;
        public GoAiController aiController;
        public GoUI ui;
        public TMP_Text panelText;
        // Production worlds keep developer counters out of the primary
        // analysis card. The generator leaves this false; a local diagnostic
        // scene may opt in without changing the synchronized search data.
        public bool showDeveloperDiagnostics=false;
        public bool showAdvancedAnalysis=false;
        // Transport accounting is local evidence, never synchronized state.
        // Udon reports the actual byte count in OnPostSerialization; the
        // estimator is a transparent raw-field approximation used before a
        // first publish has completed.
        public int serializationCount;
        public int lastSerializationByteCount;
        public bool lastSerializationSuccess;
        public int serializationFailureCount;
        public int refreshCount;

        public const int SYNCED_INT_FIELD_COUNT=20;
        public const int SYNCED_FLOAT_FIELD_COUNT=18;
        public const int SYNCED_BOOL_FIELD_COUNT=1;
        public const int SYNCED_STRING_FIELD_COUNT=11;

        public int EstimatedRawSyncedBytes()
        {
            int bytes=SYNCED_INT_FIELD_COUNT*4+SYNCED_FLOAT_FIELD_COUNT*4+
                SYNCED_BOOL_FIELD_COUNT;
            bytes+=StringBytes(statusText)+StringBytes(bestMove)+
                StringBytes(rootPriorMove)+StringBytes(candidateMove0)+
                StringBytes(candidateMove1)+StringBytes(candidateMove2)+
                StringBytes(computeOwner)+StringBytes(errorText)+
                StringBytes(finalAnalysisStatusText)+StringBytes(finalAnalysisBestMove)+
                StringBytes(finalAnalysisSnapshot);
            return bytes;
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            serializationCount++;
            lastSerializationByteCount=result.byteCount;
            lastSerializationSuccess=result.success;
            if(!result.success)serializationFailureCount++;
        }

        private int StringBytes(string value)
        { return value==null?0:value.Length*2; }
        // RefreshNow is called from the cooperative AI tick, including on
        // frames where no synchronized telemetry changed.  Cache the rendered
        // snapshot so TMP is not rebuilt every frame (or alternately written
        // by GoUI and telemetry).
        private bool hasRenderedSnapshot;
        private bool renderedEnglish;
        private bool renderedSnapshot;
        private int renderedGameRevision=-1;
        private int renderedGameSettingsRevision=-1;
        private int renderedTelemetryRevision=-1;
        private int renderedSearchPhase=-1;
        private int renderedTargetVisits=-1;
        private int renderedCompletedVisits=-1;
        private int renderedTreeNodes=-1;
        private int renderedTreeEdges=-1;
        private int renderedStaleResults=-1;
        private int renderedOutputRevision=-1;
        private int renderedMoveCount=-1;
        private const float UNRENDERED_FLOAT=-999999f;
        private float renderedRootQ=UNRENDERED_FLOAT;
        private float renderedScoreMean=UNRENDERED_FLOAT;
        private float renderedScoreStdev=UNRENDERED_FLOAT;
        private float renderedLead=UNRENDERED_FLOAT;
        private float renderedOwnershipMean=UNRENDERED_FLOAT;
        private float renderedBlackWinProbability=UNRENDERED_FLOAT;
        private float renderedWhiteWinProbability=UNRENDERED_FLOAT;
        private float renderedNoResultProbability=UNRENDERED_FLOAT;
        private string renderedStatus="";
        private string renderedBestMove="";
        private string renderedRootPriorMove="";
        private string renderedCandidateMove0="";
        private string renderedCandidateMove1="";
        private string renderedCandidateMove2="";
        private string renderedBlackPlayer="";
        private string renderedWhitePlayer="";
        private int renderedCandidateVisits0=-1;
        private int renderedCandidateVisits1=-1;
        private int renderedCandidateVisits2=-1;
        private float renderedCandidatePrior0=UNRENDERED_FLOAT;
        private float renderedCandidatePrior1=UNRENDERED_FLOAT;
        private float renderedCandidatePrior2=UNRENDERED_FLOAT;
        private float renderedCandidateValue0=UNRENDERED_FLOAT;
        private float renderedCandidateValue1=UNRENDERED_FLOAT;
        private float renderedCandidateValue2=UNRENDERED_FLOAT;
        private string renderedOwner="";
        private string renderedError="";
        private bool renderedDeveloperDiagnostics;
        private bool renderedAdvancedAnalysis;
        private bool deferredFinalAnalysisPublish;

        public void Start()
        {
            RefreshNow();
        }

        /// <summary>
        /// Copies the current authoritative controller snapshot and publishes
        /// it as one manual-sync revision. Remote clients can render it but
        /// can never overwrite it.
        /// </summary>
        public void CommitFromController()
        {
            if(game==null||aiController==null)return;
            VRCPlayerApi local=Networking.LocalPlayer;
            if(Utilities.IsValid(local)&&!Networking.IsOwner(gameObject))
                Networking.SetOwner(local,gameObject);
            if(Utilities.IsValid(local)&&!Networking.IsOwner(gameObject))return;

            GoMctsSearch search=aiController.search;
            // Publishing a hint increments the Go revision even though the
            // board position is unchanged. Keep that one-result snapshot
            // visible, but reject every other stale search tree.
            bool samePosition=search!=null&&search.rootRevision==game.revision;
            bool hintPosition=search!=null&&game.hintMove!=GoGame.NONE&&
                search.rootRevision+1==game.revision;
            // A controller can observe a newly transferred owner before the
            // old search object has been fully cancelled. Do not publish that
            // old tree as live telemetry; the final-analysis snapshot remains
            // available for readers while the new owner starts a fresh root.
            bool ownerPositionValid=search!=null&&
                search.rootOwnerLifecycle==aiController.ownerLifecycle;
            bool searchPositionValid=(samePosition||hintPosition)&&ownerPositionValid;
            bool rootScoreReady=searchPositionValid&&
                search.rootNeuralOutputRevision>0&&
                search.rootNeuralOutputRevision==search.rootRevision;
            bool rootOwnershipReady=searchPositionValid&&
                aiController.lastNeuralOwnershipOutputRevision>0&&
                aiController.lastNeuralOwnershipOutputRevision==search.rootRevision;
            controllerState=aiController.controllerState;
            searchPhase=!searchPositionValid?GoMctsSearch.PHASE_IDLE:search.phase;
            targetVisits=!searchPositionValid?0:search.targetVisits;
            completedVisits=!searchPositionValid?0:search.visitsCompleted;
            treeNodes=!searchPositionValid?0:search.treeNodeCount;
            treeEdges=!searchPositionValid?0:search.treeEdgeCount;
            staleResultsDiscarded=search==null?0:search.staleResultsDiscarded;
            outputRevision=rootScoreReady?search.rootNeuralOutputRevision:0;
            lastSearchFrames=aiController.lastSearchFrames;
            schedulerStride=aiController.schedulerStride;
            activeSearches=Mathf.Max(1,aiController.schedulerActiveSearches);
            dispatchesPerFrame=Mathf.Max(1,aiController.schedulerDispatchesPerFrame);
            observedFps=aiController.schedulerObservedFps;
            searchMilliseconds=aiController.lastSearchMilliseconds;
            progress=search==null?0f:Mathf.Clamp01(search.targetVisits>0
                ? (float)search.visitsCompleted/search.targetVisits : 0f);
            scoreMean=rootScoreReady?aiController.lastNeuralScoreMean:0f;
            scoreStdev=rootScoreReady?aiController.lastNeuralScoreStdev:0f;
            // The lead is tied to the immutable search root, not to the last
            // arbitrary leaf readback.
            lead=rootScoreReady?search.rootNeuralLead:0f;
            ownershipOutputRevision=rootOwnershipReady?search.rootRevision:0;
            ownershipMean=rootOwnershipReady?aiController.lastNeuralOwnershipMean:0f;
            blackWinProbability=aiController.lastNeuralBlackWinProbability;
            whiteWinProbability=aiController.lastNeuralWhiteWinProbability;
            noResultProbability=aiController.lastNeuralNoResultProbability;
            value=aiController.lastNeuralValue;
            rootQ=!searchPositionValid?0f:search.GetRootMeanValue();
            statusText=aiController.GetStatusText();
            bestMove=!searchPositionValid?"—":game.GetCoordinateLabel(search.bestMove);
            rootPriorMove=!searchPositionValid?"—":game.GetCoordinateLabel(search.GetRootPriorMove());
            candidateMove0=!searchPositionValid?"—":game.GetCoordinateLabel(search.GetRootCandidateMove(0));
            candidateMove1=!searchPositionValid?"—":game.GetCoordinateLabel(search.GetRootCandidateMove(1));
            candidateMove2=!searchPositionValid?"—":game.GetCoordinateLabel(search.GetRootCandidateMove(2));
            candidateVisits0=!searchPositionValid?0:search.GetRootCandidateVisits(0);
            candidateVisits1=!searchPositionValid?0:search.GetRootCandidateVisits(1);
            candidateVisits2=!searchPositionValid?0:search.GetRootCandidateVisits(2);
            candidatePrior0=!searchPositionValid?0f:search.GetRootCandidatePrior(0);
            candidatePrior1=!searchPositionValid?0f:search.GetRootCandidatePrior(1);
            candidatePrior2=!searchPositionValid?0f:search.GetRootCandidatePrior(2);
            candidateValue0=!searchPositionValid?0f:search.GetRootCandidateValue(0);
            candidateValue1=!searchPositionValid?0f:search.GetRootCandidateValue(1);
            candidateValue2=!searchPositionValid?0f:search.GetRootCandidateValue(2);
            errorText=aiController.lastError==null?"":aiController.lastError;
            computeOwner=Utilities.IsValid(local)?local.displayName:"Local Player";
            revision=revision>=2000000000?1:revision+1;
            RequestSerialization();
            RefreshNow();
        }

        /// <summary>
        /// Persists the completed/current root estimate before an AI move or
        /// hint invalidates the search token.  The values are authoritative
        /// telemetry, not a second game state, and are intentionally kept
        /// until a new root analysis is published or the game is reset.
        /// </summary>
        public void CaptureFinalAnalysis(GoMctsSearch searchSnapshot,
            GoAiController controllerSnapshot, string reason)
        {
            CaptureFinalAnalysisInternal(searchSnapshot,controllerSnapshot,reason,true);
        }

        /// <summary>
        /// Captures the completed root without immediately serializing a second
        /// telemetry packet. The AI controller uses this around an AI move so
        /// the final analysis and board mutation can be published as one
        /// post-move telemetry update instead of two back-to-back payloads.
        /// </summary>
        public void CaptureFinalAnalysisDeferred(GoMctsSearch searchSnapshot,
            GoAiController controllerSnapshot, string reason)
        {
            CaptureFinalAnalysisInternal(searchSnapshot,controllerSnapshot,reason,false);
        }

        private void CaptureFinalAnalysisInternal(GoMctsSearch searchSnapshot,
            GoAiController controllerSnapshot, string reason,bool publish)
        {
            if(game==null||searchSnapshot==null||controllerSnapshot==null)return;
            VRCPlayerApi local=Networking.LocalPlayer;
            if(Utilities.IsValid(local)&&!Networking.IsOwner(gameObject))
                Networking.SetOwner(local,gameObject);
            if(Utilities.IsValid(local)&&!Networking.IsOwner(gameObject))return;
            hasFinalAnalysis=true;
            finalAnalysisRevision=searchSnapshot.rootRevision;
            // The main progress line is the search-session counter, never a
            // root edge counter.  A completed MCTS session has already
            // incremented visitsCompleted before entering PHASE_COMPLETE; use
            // that session target as the display denominator and normalize
            // only the presentation snapshot so a finished search is always
            // rendered as target/target (8/8, 20/20, ...).  Candidate edge
            // visits below intentionally remain their raw N-1 values.
            int finalTarget=Mathf.Max(0,searchSnapshot.targetVisits);
            int finalCompleted=Mathf.Clamp(searchSnapshot.visitsCompleted,0,finalTarget);
            if(searchSnapshot.phase==GoMctsSearch.PHASE_COMPLETE&&finalTarget>0)
                finalCompleted=finalTarget;
            finalAnalysisTargetVisits=finalTarget;
            finalAnalysisCompletedVisits=finalCompleted;
            finalAnalysisStatusText=reason==null||reason.Length==0
                ?"AI analysis complete":reason;
            finalAnalysisBestMove=game.GetCoordinateLabel(ResolveEstimateMove(searchSnapshot));
            bool rootScoreReady=searchSnapshot.rootNeuralOutputRevision>0&&
                searchSnapshot.rootNeuralOutputRevision==searchSnapshot.rootRevision&&
                searchSnapshot.rootOwnerLifecycle==controllerSnapshot.ownerLifecycle;
            bool rootOwnershipReady=controllerSnapshot.lastNeuralOwnershipOutputRevision>0&&
                controllerSnapshot.lastNeuralOwnershipOutputRevision==
                    searchSnapshot.rootRevision&&
                searchSnapshot.rootOwnerLifecycle==controllerSnapshot.ownerLifecycle;
            // Copy the complete result into the scalar presentation fields as
            // well as the compact snapshot.  The post-move publication clears
            // the live-search counters, so RefreshNow must still have stable
            // values for every row of the fixed eleven-line card.
            outputRevision=rootScoreReady?searchSnapshot.rootNeuralOutputRevision:0;
            scoreMean=rootScoreReady?controllerSnapshot.lastNeuralScoreMean:0f;
            scoreStdev=rootScoreReady?controllerSnapshot.lastNeuralScoreStdev:0f;
            lead=rootScoreReady?searchSnapshot.rootNeuralLead:0f;
            ownershipOutputRevision=rootOwnershipReady?searchSnapshot.rootRevision:0;
            ownershipMean=rootOwnershipReady?controllerSnapshot.lastNeuralOwnershipMean:0f;
            blackWinProbability=controllerSnapshot.lastNeuralBlackWinProbability;
            whiteWinProbability=controllerSnapshot.lastNeuralWhiteWinProbability;
            noResultProbability=controllerSnapshot.lastNeuralNoResultProbability;
            rootQ=searchSnapshot.GetRootMeanValue();
            bestMove=finalAnalysisBestMove;
            candidateMove0=game.GetCoordinateLabel(searchSnapshot.GetRootCandidateMove(0));
            candidateMove1=game.GetCoordinateLabel(searchSnapshot.GetRootCandidateMove(1));
            candidateMove2=game.GetCoordinateLabel(searchSnapshot.GetRootCandidateMove(2));
            candidateVisits0=searchSnapshot.GetRootCandidateVisits(0);
            candidateVisits1=searchSnapshot.GetRootCandidateVisits(1);
            candidateVisits2=searchSnapshot.GetRootCandidateVisits(2);
            candidatePrior0=searchSnapshot.GetRootCandidatePrior(0);
            candidatePrior1=searchSnapshot.GetRootCandidatePrior(1);
            candidatePrior2=searchSnapshot.GetRootCandidatePrior(2);
            candidateValue0=searchSnapshot.GetRootCandidateValue(0);
            candidateValue1=searchSnapshot.GetRootCandidateValue(1);
            candidateValue2=searchSnapshot.GetRootCandidateValue(2);
            VRCPlayerApi resultOwner=Networking.GetOwner(gameObject);
            computeOwner=Utilities.IsValid(resultOwner)?resultOwner.displayName:
                (Utilities.IsValid(local)?local.displayName:"Local Player");
            string candidate0=FormatCandidateSnapshot(
                game.GetCoordinateLabel(searchSnapshot.GetRootCandidateMove(0)),
                searchSnapshot.GetRootCandidateVisits(0),
                searchSnapshot.GetRootCandidatePrior(0),
                searchSnapshot.GetRootCandidateValue(0));
            string candidate1=FormatCandidateSnapshot(
                game.GetCoordinateLabel(searchSnapshot.GetRootCandidateMove(1)),
                searchSnapshot.GetRootCandidateVisits(1),
                searchSnapshot.GetRootCandidatePrior(1),
                searchSnapshot.GetRootCandidateValue(1));
            string candidate2=FormatCandidateSnapshot(
                game.GetCoordinateLabel(searchSnapshot.GetRootCandidateMove(2)),
                searchSnapshot.GetRootCandidateVisits(2),
                searchSnapshot.GetRootCandidatePrior(2),
                searchSnapshot.GetRootCandidateValue(2));
            finalAnalysisSnapshot="CANDIDATES  "+candidate0+"  |  "+candidate1+"  |  "+candidate2+
                "\nWINRATE  B "+(controllerSnapshot.lastNeuralBlackWinProbability*100f).ToString("0.0")+
                "%  W "+(controllerSnapshot.lastNeuralWhiteWinProbability*100f).ToString("0.0")+
                "%  NR "+(controllerSnapshot.lastNeuralNoResultProbability*100f).ToString("0.0")+
                "%\nSCORE LEAD W−B "+
                (rootScoreReady?searchSnapshot.rootNeuralLead.ToString("+0.0;-0.0;0.0"):"—")+
                " pts"+
                "  WHITE OWNERSHIP MEAN "+
                    (rootOwnershipReady?controllerSnapshot.lastNeuralOwnershipMean.ToString("0.00"):"—")+
                "\n"+FormatEstimatedChineseScoreLine(true,
                    game!=null&&game.board!=null&&game.board.Length>=GoGame.AREA,
                    game==null?0f:game.ComputeCurrentWhiteMinusBlackAreaScore())+
                "\nROOT Q "+searchSnapshot.GetRootMeanValue().ToString("0.000")+
                "  POLICY "+game.GetCoordinateLabel(searchSnapshot.GetRootPriorMove())+
                "  OWNER "+(Utilities.IsValid(local)?local.displayName:"Local Player");
            deferredFinalAnalysisPublish=!publish;
            if(publish)
            {
                revision=revision>=2000000000?1:revision+1;
                RequestSerialization();
            }
            RefreshNow();
        }

        public void PublishDeferredFinalAnalysis()
        {
            if(!deferredFinalAnalysisPublish||!hasFinalAnalysis)return;
            deferredFinalAnalysisPublish=false;
            // The board move (or hint commit) has already invalidated the
            // captured search identity.  Close the synchronized live-search
            // presentation in the same packet as the final snapshot; without
            // this, a client can keep rendering the pre-commit
            // "Thinking · GPU evaluation" state and its stale N-1 progress
            // even though the move is visibly on the board.  The actual final
            // session count remains in finalAnalysisCompletedVisits/
            // finalAnalysisTargetVisits below and is rendered by RefreshNow.
            controllerState=GoAiController.STATE_IDLE;
            searchPhase=GoMctsSearch.PHASE_IDLE;
            targetVisits=0;
            completedVisits=0;
            progress=0f;
            treeNodes=0;
            treeEdges=0;
            statusText=finalAnalysisStatusText;
            revision=revision>=2000000000?1:revision+1;
            RequestSerialization();
            RefreshNow();
        }

        public void ClearFinalAnalysis()
        {
            if(!hasFinalAnalysis)return;
            VRCPlayerApi local=Networking.LocalPlayer;
            if(Utilities.IsValid(local)&&!Networking.IsOwner(gameObject))
                Networking.SetOwner(local,gameObject);
            if(Utilities.IsValid(local)&&!Networking.IsOwner(gameObject))return;
            hasFinalAnalysis=false;
            deferredFinalAnalysisPublish=false;
            finalAnalysisRevision=0;
            finalAnalysisStatusText="No completed AI analysis";
            finalAnalysisTargetVisits=0;
            finalAnalysisCompletedVisits=0;
            finalAnalysisBestMove="—";
            finalAnalysisSnapshot="";
            ownershipOutputRevision=0;
            revision=revision>=2000000000?1:revision+1;
            RequestSerialization();
            RefreshNow();
        }

        private bool IsCurrentPositionRevision(int candidateRevision)
        {
            if(game==null||candidateRevision<=0)return false;
            if(candidateRevision==game.revision)return true;
            // Publishing a hint advances the synchronized revision without
            // changing the actual board position.
            return game.hintMove!=GoGame.NONE&&candidateRevision+1==game.revision;
        }

        private bool HasLocalCurrentRootLead()
        {
            if(game==null||aiController==null||aiController.search==null)return false;
            GoMctsSearch search=aiController.search;
            return search.rootOwnerLifecycle==aiController.ownerLifecycle&&
                search.rootNeuralOutputRevision>0&&
                search.rootNeuralOutputRevision==search.rootRevision&&
                IsCurrentPositionRevision(search.rootRevision);
        }

        public bool HasCurrentRootScoreLead()
        {
            if(HasLocalCurrentRootLead())return true;
            return outputRevision>0&&IsCurrentPositionRevision(outputRevision);
        }

        public float GetCurrentRootScoreLeadWhiteMinusBlack()
        {
            if(HasLocalCurrentRootLead())return aiController.search.rootNeuralLead;
            return lead;
        }

        private int ResolveEstimateMove(GoMctsSearch searchSnapshot)
        {
            if(searchSnapshot==null)return GoGame.NONE;
            int move=searchSnapshot.bestMove;
            if(move==GoGame.NONE)move=searchSnapshot.GetRootCandidateMove(0);
            if(move==GoGame.NONE)move=searchSnapshot.GetRootPriorMove();
            return move;
        }

        private string FormatCandidateSnapshot(string move,int visits,float prior,float value)
        {
            if(move==null||move.Length==0||move=="—"||visits<=0)return "—";
            return move+" "+visits+"v "+(prior*100f).ToString("0.0")+
                "% Q"+value.ToString("+0.000;-0.000;0.000");
        }

        private string FormatEstimatedChineseScoreLine(bool english,
            bool boardReady,float whiteMinusBlackPoints)
        {
            if(!boardReady)
                return english?"STATIC CN SCORE W−B —":
                    "预计子差 白−黑 —";
            float estimatedZi=whiteMinusBlackPoints*0.5f;
            return english?"STATIC CN SCORE W−B "+
                estimatedZi.ToString("+0.00;-0.00;0.00")+" zi":
                "预计子差 白−黑 "+
                estimatedZi.ToString("+0.00;-0.00;0.00")+" 子";
        }

        public void RefreshNow()
        {
            if(game==null)return;
            bool snapshot=revision>0;
            bool english=game.ui==null||game.ui.englishLanguage;
            GoMctsSearch search=aiController==null?null:aiController.search;
            int shownTarget=snapshot?targetVisits:(search==null?0:search.targetVisits);
            int shownCompleted=snapshot?completedVisits:(search==null?0:search.visitsCompleted);
            int shownNodes=snapshot?treeNodes:(search==null?0:search.treeNodeCount);
            int shownEdges=snapshot?treeEdges:(search==null?0:search.treeEdgeCount);
            int shownStale=snapshot?staleResultsDiscarded:(search==null?0:search.staleResultsDiscarded);
            float shownRootQ=snapshot?rootQ:(search==null?0f:search.GetRootMeanValue());
            string shownStatus=snapshot
                ?LocalizeStatusForLanguage(statusText,english)
                :(aiController==null?(english?"AI offline":"AI 离线"):
                    aiController.GetStatusTextLocalized(english));
            string shownBest=snapshot?bestMove:(search==null?"—":game.GetCoordinateLabel(search.bestMove));
            string shownOwner=snapshot?computeOwner:GetOwnerName();
            string shownPrior=snapshot?rootPriorMove:(search==null?"—":game.GetCoordinateLabel(search.GetRootPriorMove()));
            string shownCandidate0=snapshot?candidateMove0:(search==null?"—":game.GetCoordinateLabel(search.GetRootCandidateMove(0)));
            string shownCandidate1=snapshot?candidateMove1:(search==null?"—":game.GetCoordinateLabel(search.GetRootCandidateMove(1)));
            string shownCandidate2=snapshot?candidateMove2:(search==null?"—":game.GetCoordinateLabel(search.GetRootCandidateMove(2)));
            int shownCandidateVisits0=snapshot?candidateVisits0:(search==null?0:search.GetRootCandidateVisits(0));
            int shownCandidateVisits1=snapshot?candidateVisits1:(search==null?0:search.GetRootCandidateVisits(1));
            int shownCandidateVisits2=snapshot?candidateVisits2:(search==null?0:search.GetRootCandidateVisits(2));
            float shownCandidatePrior0=snapshot?candidatePrior0:(search==null?0f:search.GetRootCandidatePrior(0));
            float shownCandidatePrior1=snapshot?candidatePrior1:(search==null?0f:search.GetRootCandidatePrior(1));
            float shownCandidatePrior2=snapshot?candidatePrior2:(search==null?0f:search.GetRootCandidatePrior(2));
            float shownCandidateValue0=snapshot?candidateValue0:(search==null?0f:search.GetRootCandidateValue(0));
            float shownCandidateValue1=snapshot?candidateValue1:(search==null?0f:search.GetRootCandidateValue(1));
            float shownCandidateValue2=snapshot?candidateValue2:(search==null?0f:search.GetRootCandidateValue(2));
            bool liveSearch=searchPhase==GoMctsSearch.PHASE_WAITING_NEURAL||
                searchPhase==GoMctsSearch.PHASE_EXPANDING||
                searchPhase==GoMctsSearch.PHASE_SELECTING||
                searchPhase==GoMctsSearch.PHASE_BACKING_UP||
                searchPhase==GoMctsSearch.PHASE_RUNNING||
                (aiController!=null&&aiController.hintRequested);
            bool useFinalAnalysis=!liveSearch&&hasFinalAnalysis;
            bool liveAnalysisLeadReady=HasCurrentRootScoreLead();
            bool finalAnalysisRootLeadReady=useFinalAnalysis&&
                hasFinalAnalysis&&finalAnalysisRevision>0&&
                outputRevision==finalAnalysisRevision;
            bool displayedAnalysisLeadReady=liveAnalysisLeadReady||
                finalAnalysisRootLeadReady;
            float displayedAnalysisLead=liveAnalysisLeadReady
                ?GetCurrentRootScoreLeadWhiteMinusBlack()
                :(finalAnalysisRootLeadReady?lead:0f);
            bool localLiveOwnershipReady=aiController!=null&&search!=null&&
                aiController.lastNeuralOwnershipOutputRevision>0&&
                aiController.lastNeuralOwnershipOutputRevision==search.rootRevision&&
                search.rootOwnerLifecycle==aiController.ownerLifecycle&&
                IsCurrentPositionRevision(search.rootRevision);
            bool syncedLiveOwnershipReady=snapshot&&!useFinalAnalysis&&
                ownershipOutputRevision>0&&
                IsCurrentPositionRevision(ownershipOutputRevision);
            bool liveAnalysisOwnershipReady=localLiveOwnershipReady||
                syncedLiveOwnershipReady;
            bool finalAnalysisOwnershipReady=useFinalAnalysis&&
                hasFinalAnalysis&&finalAnalysisRevision>0&&
                ownershipOutputRevision==finalAnalysisRevision;
            bool displayedOwnershipReady=liveAnalysisOwnershipReady||
                finalAnalysisOwnershipReady;
            float displayedOwnershipMean=localLiveOwnershipReady
                ?aiController.lastNeuralOwnershipMean
                :(displayedOwnershipReady?ownershipMean:0f);
            bool currentBoardScoreReady=game.board!=null&&
                game.board.Length>=GoGame.AREA;
            float currentBoardWhiteMinusBlackPoints=currentBoardScoreReady
                ?game.ComputeCurrentWhiteMinusBlackAreaScore():0f;
            float shownScoreMean=snapshot?scoreMean:0f;
            float shownScoreStdev=snapshot?scoreStdev:0f;
            float shownLead=snapshot?lead:0f;
            float shownOwnershipMean=snapshot?ownershipMean:0f;
            float shownBlackWinProbability=snapshot?blackWinProbability:0f;
            float shownWhiteWinProbability=snapshot?whiteWinProbability:0f;
            float shownNoResultProbability=snapshot?noResultProbability:0f;
            int shownOutputRevision=snapshot?outputRevision:0;
            bool localRootScoreReady=!snapshot&&aiController!=null&&search!=null&&
                search.rootNeuralOutputRevision>0&&
                search.rootNeuralOutputRevision==search.rootRevision&&
                search.rootOwnerLifecycle==aiController.ownerLifecycle&&
                IsCurrentPositionRevision(search.rootRevision);
            if(localRootScoreReady)
            {
                // The local owner can render freshly completed NN output
                // before the next manual-sync packet.  Remote clients keep
                // using the synchronized snapshot above.
                shownOutputRevision=search.rootNeuralOutputRevision;
                shownScoreMean=aiController.lastNeuralScoreMean;
                shownScoreStdev=aiController.lastNeuralScoreStdev;
                shownLead=aiController.lastNeuralLead;
                if(localLiveOwnershipReady)
                    shownOwnershipMean=aiController.lastNeuralOwnershipMean;
                shownBlackWinProbability=aiController.lastNeuralBlackWinProbability;
                shownWhiteWinProbability=aiController.lastNeuralWhiteWinProbability;
                shownNoResultProbability=aiController.lastNeuralNoResultProbability;
            }
            if(useFinalAnalysis)
            {
                shownTarget=Mathf.Max(0,finalAnalysisTargetVisits);
                shownCompleted=Mathf.Clamp(finalAnalysisCompletedVisits,0,shownTarget);
                shownStatus=LocalizeStatusForLanguage(finalAnalysisStatusText,english);
                shownBest=finalAnalysisBestMove;
                // The final result has already been copied into the same
                // scalar fields used by the live card.  The packed snapshot is
                // retained for compatibility/recovery, but the renderer below
                // always emits the fixed eleven-line template.
            }
            if(!liveSearch&&!useFinalAnalysis)
            {
                shownOwner="—";
                shownBest="—";
                shownCandidate0="—";shownCandidate1="—";shownCandidate2="—";
                shownCandidateVisits0=0;shownCandidateVisits1=0;shownCandidateVisits2=0;
                shownCandidatePrior0=0f;shownCandidatePrior1=0f;shownCandidatePrior2=0f;
                shownCandidateValue0=0f;shownCandidateValue1=0f;shownCandidateValue2=0f;
                shownScoreMean=0f;shownScoreStdev=0f;shownLead=0f;
                shownOwnershipMean=0f;shownBlackWinProbability=0f;
                shownWhiteWinProbability=0f;shownNoResultProbability=0f;
                shownOutputRevision=0;
            }
            string blackPlayer=GetPlayerDisplayName(game.blackIsAI,game.blackPlayerName,english);
            string whitePlayer=GetPlayerDisplayName(game.whiteIsAI,game.whitePlayerName,english);
            if(shownStatus==null||shownStatus.Length==0)
                shownStatus=english?"AI ready":"AI 就绪";
            shownStatus=SingleLine(shownStatus);
            shownOwner=SingleLine(shownOwner);
            string lastMove=game.lastMove>=0&&game.lastMove<GoGame.AREA
                ?game.GetCoordinateLabel(game.lastMove):"—";
            bool outputReady=useFinalAnalysis||
                (shownOutputRevision>0&&shownOutputRevision==game.revision);
            string level=difficulty==null?(english?"—":"—"):
                difficulty.GetPresetLabelLocalized(english);
            string renderError=errorText==null?"":errorText;
            if(!useFinalAnalysis&&renderError.Length>0)
                shownStatus=english?"AI error: "+SingleLine(renderError):"AI 错误："+SingleLine(renderError);
            bool dirty=!hasRenderedSnapshot||renderedEnglish!=english||renderedSnapshot!=snapshot||
                renderedDeveloperDiagnostics!=showDeveloperDiagnostics||
                renderedAdvancedAnalysis!=showAdvancedAnalysis||
                renderedGameRevision!=game.revision||renderedGameSettingsRevision!=game.settingsRevision||
                renderedTelemetryRevision!=revision||renderedSearchPhase!=searchPhase||
                renderedTargetVisits!=shownTarget||renderedCompletedVisits!=shownCompleted||
                renderedTreeNodes!=shownNodes||renderedTreeEdges!=shownEdges||
                renderedStaleResults!=shownStale||renderedOutputRevision!=shownOutputRevision||
                renderedMoveCount!=game.moveCount||renderedRootQ!=shownRootQ||
                renderedScoreMean!=shownScoreMean||renderedScoreStdev!=shownScoreStdev||
                renderedLead!=shownLead||renderedOwnershipMean!=shownOwnershipMean||
                renderedBlackWinProbability!=shownBlackWinProbability||
                renderedWhiteWinProbability!=shownWhiteWinProbability||
                renderedNoResultProbability!=shownNoResultProbability||
                renderedStatus!=shownStatus||renderedBestMove!=shownBest||
                renderedRootPriorMove!=shownPrior||
                renderedCandidateMove0!=shownCandidate0||renderedCandidateMove1!=shownCandidate1||
                renderedCandidateMove2!=shownCandidate2||
                renderedCandidateVisits0!=shownCandidateVisits0||renderedCandidateVisits1!=shownCandidateVisits1||
                renderedCandidateVisits2!=shownCandidateVisits2||
                renderedCandidatePrior0!=shownCandidatePrior0||renderedCandidatePrior1!=shownCandidatePrior1||
                renderedCandidatePrior2!=shownCandidatePrior2||
                renderedCandidateValue0!=shownCandidateValue0||renderedCandidateValue1!=shownCandidateValue1||
                renderedCandidateValue2!=shownCandidateValue2||
                renderedOwner!=shownOwner||renderedBlackPlayer!=blackPlayer||
                renderedWhitePlayer!=whitePlayer||
                renderedError!=renderError;
            if(!dirty)return;
            hasRenderedSnapshot=true;
            renderedEnglish=english;
            renderedSnapshot=snapshot;
            renderedGameRevision=game.revision;
            renderedGameSettingsRevision=game.settingsRevision;
            renderedTelemetryRevision=revision;
            renderedSearchPhase=searchPhase;
            renderedTargetVisits=shownTarget;
            renderedCompletedVisits=shownCompleted;
            renderedTreeNodes=shownNodes;
            renderedTreeEdges=shownEdges;
            renderedStaleResults=shownStale;
            renderedOutputRevision=shownOutputRevision;
            renderedMoveCount=game.moveCount;
            renderedRootQ=shownRootQ;
            renderedScoreMean=shownScoreMean;
            renderedScoreStdev=shownScoreStdev;
            renderedLead=shownLead;
            renderedOwnershipMean=shownOwnershipMean;
            renderedBlackWinProbability=shownBlackWinProbability;
            renderedWhiteWinProbability=shownWhiteWinProbability;
            renderedNoResultProbability=shownNoResultProbability;
            renderedStatus=shownStatus;
            renderedBestMove=shownBest;
            renderedRootPriorMove=shownPrior;
            renderedCandidateMove0=shownCandidate0;
            renderedCandidateMove1=shownCandidate1;
            renderedCandidateMove2=shownCandidate2;
            renderedCandidateVisits0=shownCandidateVisits0;
            renderedCandidateVisits1=shownCandidateVisits1;
            renderedCandidateVisits2=shownCandidateVisits2;
            renderedCandidatePrior0=shownCandidatePrior0;
            renderedCandidatePrior1=shownCandidatePrior1;
            renderedCandidatePrior2=shownCandidatePrior2;
            renderedCandidateValue0=shownCandidateValue0;
            renderedCandidateValue1=shownCandidateValue1;
            renderedCandidateValue2=shownCandidateValue2;
            renderedOwner=shownOwner;
            renderedBlackPlayer=blackPlayer;
            renderedWhitePlayer=whitePlayer;
            renderedError=renderError;
            renderedDeveloperDiagnostics=showDeveloperDiagnostics;
            renderedAdvancedAnalysis=showAdvancedAnalysis;
            refreshCount++;
            string valueText=(english?"AI STATE  ":"AI 状态  ")+shownStatus+
                "\n"+(english?"SEARCH  ":"搜索  ")+shownCompleted+"/"+shownTarget+
                " visits"+
                "\n"+(english?"BEST MOVE  ":"推荐着法  ")+shownBest+
                "\n"+(english?"CANDIDATES  ":"候选着法 ")+
                    FormatCandidate(shownCandidate0,shownCandidateVisits0,shownCandidatePrior0,shownCandidateValue0,english)+
                    "  |  "+FormatCandidate(shownCandidate1,shownCandidateVisits1,shownCandidatePrior1,shownCandidateValue1,english)+
                    "  |  "+FormatCandidate(shownCandidate2,shownCandidateVisits2,shownCandidatePrior2,shownCandidateValue2,english)+
                "\n"+(english?"WINRATE  B ":"胜率  黑 ")+
                    (outputReady?(shownBlackWinProbability*100f).ToString("0.0")+"%":"—")+
                    (english?"  W ":"  白 ")+
                    (outputReady?(shownWhiteWinProbability*100f).ToString("0.0")+"%":"—")+
                    (english?"  NR ":"  无结果 ")+
                    (outputReady?(shownNoResultProbability*100f).ToString("0.0")+"%":"—")+
                "\n"+(english?"SCORE LEAD W−B ":"预计分差 白−黑 ")+
                    (displayedAnalysisLeadReady?displayedAnalysisLead.ToString("+0.0;-0.0;0.0"):"—")+
                    (english?" pts":" 点")+
                "\n"+(english?"WHITE OWNERSHIP MEAN ":"白方领地均值 ")+
                    (displayedOwnershipReady?displayedOwnershipMean.ToString("0.00"):"—")+
                "\n"+(english?"LEVEL  ":"档位  ")+level+
                    (english?"  ·  CAPTURES B ":"  ·  提子 黑 ")+game.blackCaptures+
                    (english?"  W ":"  白 ")+game.whiteCaptures+
                "\n"+(english?"MOVE  ":"手数 ")+game.moveCount+
                    (english?"  ·  LAST ":"  ·  上一手 ")+lastMove+
                "\n"+FormatEstimatedChineseScoreLine(english,
                    currentBoardScoreReady,currentBoardWhiteMinusBlackPoints)+
                "\n"+(english?"COMPUTE  ":"计算端 ")+shownOwner+
                    (english?"  ·  BLACK ":"  ·  黑方 ")+blackPlayer+
                    (english?"  |  WHITE ":"  |  白方 ")+whitePlayer;
            if(panelText!=null&&panelText.text!=valueText)panelText.text=valueText;
        }

        private string SingleLine(string value)
        {
            if(value==null||value.Length==0)return "—";
            return value.Replace("\r"," ").Replace("\n"," ");
        }

        private string GetPlayerDisplayName(bool isAI,string playerName,bool english)
        {
            if(isAI)return "AI";
            if(playerName==null||playerName.Length==0)return english?"Open":"空位";
            return SingleLine(playerName);
        }

        public void ToggleAdvancedAnalysis()
        {
            showAdvancedAnalysis=!showAdvancedAnalysis;
            hasRenderedSnapshot=false;
            RefreshNow();
        }

        private string FormatCandidate(string move,int visits,float prior,float candidateValue,bool english)
        {
            if(move==null||move.Length==0||move=="—"||visits<=0)return "—";
            return move+" "+visits+(english?"v ":"次 ")+(prior*100f).ToString("0.0")+
                "% "+(english?"Q":"Q")+candidateValue.ToString("+0.000;-0.000;0.000");
        }

        private string LocalizeStatus(string value)
        {
            if(value==null)return "";
            value=value.Replace("AI error: ","AI 错误：");
            value=value.Replace("AI preparing GPU","AI 正在准备 GPU");
            value=value.Replace("AI Hint · GPU evaluation","AI 提示 · GPU 计算");
            value=value.Replace("AI Hint · legal move expansion","AI 提示 · 展开合法着法");
            value=value.Replace("AI Hint · selecting line","AI 提示 · 选择变化");
            value=value.Replace("AI Hint · visit ","AI 提示 · 搜索 ");
            value=value.Replace("AI Hint · backing up value","AI 提示 · 回传局面价值");
            value=value.Replace("AI Hint · finalizing","AI 提示 · 整理结果");
            value=value.Replace("AI Hint · searching","AI 提示 · 搜索中");
            value=value.Replace("AI Hint · disabled","AI 提示 · 未启用");
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
            value=value.Replace("AI analysis complete","AI 分析完成");
            value=value.Replace("AI hint analysis complete","AI 提示分析完成");
            value=value.Replace("AI immediate estimate","AI 即时估计");
            value=value.Replace("No completed AI analysis","尚未完成 AI 分析");
            return value;
        }

        private string LocalizeStatusForLanguage(string value,bool english)
        {
            if(!english)return LocalizeStatus(value);
            if(value==null)return "";
            value=value.Replace("AI 错误：","AI error: ");
            value=value.Replace("AI 正在准备 GPU","AI preparing GPU");
            value=value.Replace("AI 提示 · GPU 计算","AI Hint · GPU evaluation");
            value=value.Replace("AI 提示 · 展开合法着法","AI Hint · legal move expansion");
            value=value.Replace("AI 提示 · 选择变化","AI Hint · selecting line");
            value=value.Replace("AI 提示 · 回传局面价值","AI Hint · backing up value");
            value=value.Replace("AI 提示 · 搜索 ","AI Hint · visit ");
            value=value.Replace("AI 提示 · 整理结果","AI Hint · finalizing");
            value=value.Replace("AI 提示 · 搜索中","AI Hint · searching");
            value=value.Replace("AI 提示 · 未启用","AI Hint · disabled");
            value=value.Replace("玩家 vs 玩家 · 等待开始","Player vs Player · Ready to start");
            value=value.Replace("玩家 vs AI · 等待开始","Player vs AI · Ready to start");
            value=value.Replace("AI vs AI · 等待开始","AI vs AI · Ready to start");
            value=value.Replace("等待玩家","Waiting for player");
            value=value.Replace("思考中 · GPU 回读","Thinking · GPU readback");
            value=value.Replace("思考中 · GPU 计算","Thinking · GPU evaluation");
            value=value.Replace("思考中 · 展开合法着法","Thinking · expanding legal moves");
            value=value.Replace("思考中 · 选择变化","Thinking · selecting line");
            value=value.Replace("思考中 · 回传局面价值","Thinking · backing up value");
            value=value.Replace("思考中 · 搜索 ","Thinking · visit ");
            value=value.Replace("选择着法 · ","Choosing move · ");
            value=value.Replace(" 次访问"," visits");
            value=value.Replace("AI 就绪","AI ready");
            value=value.Replace("AI 不可用","AI unavailable");
            value=value.Replace("AI 分析完成","AI analysis complete");
            value=value.Replace("AI 提示分析完成","AI hint analysis complete");
            value=value.Replace("AI 即时估计","AI immediate estimate");
            value=value.Replace("尚未完成 AI 分析","No completed AI analysis");
            return value;
        }

        private string LocalizeAnalysisSnapshot(string value,bool english)
        {
            if(value==null)return value;
            if(english)
            {
                // A final snapshot can arrive after a remote client changed
                // language. Normalize known Chinese diagnostic tokens back to
                // the English template instead of leaving a mixed-language
                // center analysis card.
                value=value.Replace("候选着法", "CANDIDATES");
                value=value.Replace("候选", "CANDIDATES");
                value=value.Replace("胜率  黑", "WINRATE  B");
                value=value.Replace("  白 ", "  W ");
                value=value.Replace("  无结果 ", "  NR ");
                value=value.Replace("预计分差 白−黑", "SCORE LEAD W−B");
                value=value.Replace("白方领地均值", "WHITE OWNERSHIP MEAN");
                value=value.Replace("折合子差 白−黑", "CHINESE SCORE EQ. W−B");
                value=value.Replace("预计子差 白−黑", "STATIC CN SCORE W−B");
                value=value.Replace("终局子差 白−黑", "FINAL CN SCORE W−B");
                value=value.Replace("子差 白−黑", "CN SCORE W−B");
                // Accept snapshots produced by the previous label spelling.
                value=value.Replace("预计子差（中国） 白−黑", "STATIC CN SCORE W−B");
                value=value.Replace("终局子差（中国） 白−黑", "FINAL CN SCORE W−B");
                value=value.Replace("子差（中国） 白−黑", "CN SCORE W−B");
                value=value.Replace(" · 认输", " · resignation");
                value=value.Replace(" · 协议和棋", " · agreed draw");
                value=value.Replace(" 子", " zi");
                value=value.Replace("计算端", "COMPUTE");
                value=value.Replace("根 Q", "ROOT Q");
                value=value.Replace("策略", "POLICY");
                value=value.Replace("所有者", "OWNER");
                return value;
            }
            value=value.Replace("CANDIDATES", "候选着法");
            value=value.Replace("WINRATE  B", "胜率  黑");
            value=value.Replace("  W ", "  白 ");
            value=value.Replace("  NR ", "  无结果 ");
            value=value.Replace("SCORE LEAD W−B", "预计分差 白−黑");
            value=value.Replace("WHITE OWNERSHIP MEAN", "白方领地均值");
            value=value.Replace("CHINESE SCORE EQ. W−B", "折合子差 白−黑");
            value=value.Replace("STATIC CN SCORE W−B", "预计子差 白−黑");
            value=value.Replace("FINAL CN SCORE W−B", "终局子差 白−黑");
            value=value.Replace("CN SCORE W−B", "子差 白−黑");
            // Accept snapshots produced by the previous label spelling.
            value=value.Replace("EST. CHINESE SCORE W−B", "预计子差 白−黑");
            value=value.Replace("FINAL CHINESE SCORE W−B", "终局子差 白−黑");
            value=value.Replace("CHINESE SCORE W−B", "子差 白−黑");
            value=value.Replace(" · resignation", " · 认输");
            value=value.Replace(" · agreed draw", " · 协议和棋");
            value=value.Replace(" zi", " 子");
            value=value.Replace("COMPUTE", "计算端");
            value=value.Replace("ROOT Q", "根 Q");
            value=value.Replace("POLICY", "策略");
            value=value.Replace("OWNER", "所有者");
            return value;
        }

        private string GetOwnerName()
        {
            if(game==null)return "—";
            VRCPlayerApi owner=Networking.GetOwner(game.gameObject);
            return Utilities.IsValid(owner)?SingleLine(owner.displayName):"—";
        }

        public override void OnDeserialization()
        {
            if(ui!=null)ui.RefreshNow();
            RefreshNow();
        }
    }
}
