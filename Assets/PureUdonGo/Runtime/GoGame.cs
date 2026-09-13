using UdonSharp;
using UnityEngine;
using VRC.Udon.Common;
using VRC.SDKBase;

namespace PureUdonGo
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GoGame : UdonSharpBehaviour
    {
        public const int SIZE=19, AREA=361, EMPTY=0, BLACK=1, WHITE=-1, PASS=-1, NONE=-2;
        public const int STATE_PLAYING=0, STATE_BLACK_WINS=1, STATE_WHITE_WINS=2, STATE_DRAW=3, STATE_RESIGNED=4;
        public const int MAX_MOVES=2048, MAX_POSITION_HISTORY=2049;
        // Only these arrays are authoritative network payload. Previous board
        // planes remain synchronized because they are direct KataGo inputs;
        // positional hashes and stone-count indexes are local derived caches.
        public const int SYNCED_INT_ARRAY_COUNT=AREA*3+10+MAX_MOVES;
        public const int SYNCED_INT_ARRAY_RAW_BYTES=SYNCED_INT_ARRAY_COUNT*4;
        // This is the GoGame int-array lower bound only. It intentionally does
        // not pretend to include Udon scalar/string overhead from GoGame,
        // GoAiSettings, GoTelemetry or GoBoardPool.
        public const int EstimatedGoGameIntArrayBytes=SYNCED_INT_ARRAY_RAW_BYTES;
        public const int EstimatedRawSyncedBytes=EstimatedGoGameIntArrayBytes;
        public const int LegacyRawSyncedBytes=(AREA*3+10+MAX_MOVES+
            MAX_POSITION_HISTORY*5)*4;
        private const int MOVE_HISTORY_LOCATION_MASK=511;
        private const int MOVE_HISTORY_WHITE_MASK=512;
        public const int AUDIO_NONE=0, AUDIO_PLACE=1, AUDIO_CAPTURE=2, AUDIO_PASS=3, AUDIO_GAME_END=4, AUDIO_RESET=5;
        // Server-time based recovery window for a stalled live turn. The
        // synchronized timestamp makes the permission decision consistent on
        // every client without relying on local frame time.
        public const int FORCE_RESET_STALL_SECONDS=50;
        // Default room-wide hover-mask slice. The pool may clamp the point
        // safety quota independently, but every production caller uses this
        // time budget so legality work cannot become a post-move burst.
        public const float MOVE_MASK_WARMUP_DEFAULT_MS=0.35f;
        public const float MOVE_MASK_WARMUP_MIN_MS=0.25f;
        public const float MOVE_MASK_WARMUP_MAX_MS=0.50f;

        [Header("KataGo Tromp-Taylor-ish production rules")]
        public bool positionalSuperko=true;
        public bool areaScoring=true;
        public bool multiStoneSuicideLegal=true;
        [UdonSynced] public int komiTimes2=15;

        [UdonSynced] public int[] board=new int[AREA];
        [UdonSynced] public int[] previousBoard1=new int[AREA];
        [UdonSynced] public int[] previousBoard2=new int[AREA];
        [UdonSynced] public int[] recentMoveLoc=new int[5];
        [UdonSynced] public int[] recentMovePla=new int[5];
        // The four hash lanes are derived from the authoritative board plus
        // moveHistory after a network snapshot arrives. Keeping them local
        // removes roughly 32 KiB of raw payload per serialization while
        // preserving exact positional-superko checks. Do not mark these
        // arrays UdonSynced again without re-running the payload gate.
        public int[] hash0=new int[MAX_POSITION_HISTORY];
        public int[] hash1=new int[MAX_POSITION_HISTORY];
        public int[] hash2=new int[MAX_POSITION_HISTORY];
        public int[] hash3=new int[MAX_POSITION_HISTORY];
        // One packed entry per accepted placement/pass.  The low nine bits
        // encode location + 2 (PASS=-1 becomes 1); bit 9 stores White.  This
        // is enough to rebuild any accepted position without synchronizing
        // 2048 full board snapshots.
        [UdonSynced] public int[] moveHistory=new int[MAX_MOVES];
        // A conservative superko fast-reject index. It is rebuilt locally from
        // moveHistory on every received snapshot, so a missing/legacy index is
        // never trusted as independent authority input. The
        // accepted move log is the synchronized rule history; the index is a
        // cache, not an independent authority input.
        public int[] historyStoneCount=new int[MAX_POSITION_HISTORY];
        public int historyStoneCountVersion;
        // Exact local frequency index for the derived stone-count history.
        // Superko probes can reject impossible stone-count matches before
        // touching the four-lane hash history. It is rebuilt whenever a
        // compact snapshot or a transactional replay replaces the prefix.
        private int[] historyStoneCountFrequency=new int[AREA+1];
        private bool historyStoneCountFrequencyValid;
        public int historyStoneCountIndexRebuilds;
        [UdonSynced] public int positionHistoryCount;
        [UdonSynced] public int sideToMove=BLACK, moveCount, consecutivePasses, blackCaptures, whiteCaptures;
        // `revision` identifies the authoritative Go position/lifecycle.
        // `settingsRevision` mirrors GoAiSettings.configRevision locally. It
        // is deliberately not part of the position snapshot or network
        // authority domain; only GoAiSettings serializes configuration.
        [UdonSynced] public int gameState=STATE_PLAYING, winner=EMPTY, lastMove=NONE, koLoc=NONE, revision, handicapStones;
        [UdonSynced] public int turnStartedServerSecond;
        public int settingsRevision;
        [UdonSynced] public int finalBlackArea, finalWhiteArea;
        [UdonSynced] public float finalWhiteMinusBlackScore;
        [UdonSynced] public string lastActionText="Ready";
        [UdonSynced] public int audioEventRevision;
        [UdonSynced] public int audioEventType=AUDIO_NONE;
        [UdonSynced] public int hintMove=NONE;
        [UdonSynced] public int hintRevision;
        // AI hints follow the Xiangqi-style pre-match permission lock.  The
        // permission is authoritative and synchronized so every client sees
        // the same enabled/locked state; it cannot be enabled mid-match.
        [UdonSynced] public bool aiHintsEnabled;
        [UdonSynced] public bool aiHintPermissionLocked;
        [UdonSynced] public int aiHintPermissionRevision;
        [UdonSynced] public int undoOfferSide=EMPTY;
        [UdonSynced] public int undoOfferRevision;
        [UdonSynced] public int undoOfferMoveCount=-1;
        [UdonSynced] public int drawOfferSide=EMPTY;
        [UdonSynced] public int drawOfferRevision;
        [UdonSynced] public int drawOfferMoveCount=-1;

        [Header("Xiangqi-family synchronized match controller state")]
        [UdonSynced] public bool matchStarted;
        [UdonSynced] public bool blackIsAI;
        [UdonSynced] public bool whiteIsAI=true;
        [UdonSynced] public int starter=BLACK;
        [UdonSynced] public int humanSide=BLACK;
        [UdonSynced] public int blackPlayerId=-1;
        [UdonSynced] public int whitePlayerId=-1;
        [UdonSynced] public string blackPlayerName="";
        [UdonSynced] public string whitePlayerName="";
        [UdonSynced] public int aiControlAuthorityPlayerId=-1;
        [UdonSynced] public string aiControlAuthorityName="";
        [UdonSynced] public int networkRecoveryRevision;

        // Local transport diagnostics populated by the actual VRChat
        // serialization callback. These are not synchronized game state.
        public int serializationCount;
        public int lastSerializationByteCount;
        public bool lastSerializationSuccess;
        public int serializationFailureCount;

        public GoBoardView view;
        public GoBoardPool boardPool;
        public int tableIndex=-1;
        public GoUI ui;
        public GoTelemetry telemetry;
        public GoAiController aiController;
        public GoAiSettings aiSettings;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public AudioSource audioSource;
        public AudioClip placeClip;
        public AudioClip captureClip;
        public AudioClip passClip;
        public AudioClip gameEndClip;
        public AudioClip resetClip;

        private int[] backup=new int[AREA], group=new int[AREA], queue=new int[AREA];
        // Exact group/liberty scratch shared by authoritative rule probes.
        // Generation stamps avoid clearing two 361-element marker arrays for
        // every legal-move/capture query while preserving traversal order.
        private int[] groupVisitedStamp=new int[AREA];
        private int[] libertyVisitedStamp=new int[AREA];
        private int groupGeneration=1;
        private int collectedGroupLibertyCount;
        private bool[] regionVisited=new bool[AREA];
        private int h0,h1,h2,h3;
        private int currentStoneCount;
        // Offline verifier replay coalesces publication only during fixture
        // setup. Production TryPlay/TryPlayFromAI calls never set this flag.
        private bool suppressReplayPublication;

        // Read by the local board view to distinguish a real capture event
        // from reset/undo/recovery board replacement.
        public int lastAudioEventRevision=-1;
        private float nextLocalDrawRequestTime;
        private int[] rebuildBoardSnapshot=new int[AREA];
        private int[] rebuildPreviousBoard1Snapshot=new int[AREA];
        private int[] rebuildPreviousBoard2Snapshot=new int[AREA];
        private int[] rebuildRecentMoveLocSnapshot=new int[5];
        private int[] rebuildRecentMovePlaSnapshot=new int[5];
        private int[] rebuildMoveHistorySnapshot;
        private int[] rebuildHash0Snapshot;
        private int[] rebuildHash1Snapshot;
        private int[] rebuildHash2Snapshot;
        private int[] rebuildHash3Snapshot;
        private int[] rebuildStoneCountSnapshot;
        private int[] rebuildStoneCountFrequencySnapshot;
        private int rebuildSnapshotPositionHistoryCount;
        private int rebuildSnapshotSideToMove;
        private int rebuildSnapshotMoveCount;
        private int rebuildSnapshotConsecutivePasses;
        private int rebuildSnapshotBlackCaptures;
        private int rebuildSnapshotWhiteCaptures;
        private int rebuildSnapshotGameState;
        private int rebuildSnapshotWinner;
        private int rebuildSnapshotLastMove;
        private int rebuildSnapshotKoLoc;
        private int rebuildSnapshotFinalBlackArea;
        private int rebuildSnapshotFinalWhiteArea;
        private float rebuildSnapshotFinalScore;
        private string rebuildSnapshotAction;
        private int rebuildSnapshotHistoryStoneCountVersion;
        private int rebuildSnapshotH0;
        private int rebuildSnapshotH1;
        private int rebuildSnapshotH2;
        private int rebuildSnapshotH3;
        private int rebuildSnapshotCurrentStoneCount;
        private bool rebuildSnapshotStoneCountFrequencyValid;
        public int transactionalRebuildAttempts;
        public int transactionalRebuildFailures;
        public int transactionalRebuildRestores;
        public int lastRebuildHistoryEntriesCleared;
        private bool derivedHistoryReady;
        private bool derivedHistoryRebuildFailed;
        private int derivedHistoryRevision=-1;
        private int derivedHistoryMoveCount=-1;
        private int derivedHistoryPositionHistoryCount=-1;
        private int derivedHistoryHash0;
        private int derivedHistoryHash1;
        private int derivedHistoryHash2;
        private int derivedHistoryHash3;
        private int[] moveMaskCache=new int[AREA];
        // A generation stamp avoids clearing 361 flags synchronously on the
        // first hover after every position change.  The cache itself is still
        // populated incrementally by StepMoveMaskCache below.
        private int[] moveMaskComputedGeneration=new int[AREA];
        private int moveMaskCacheGeneration;
        private bool moveMaskCacheValid;
        private bool moveMaskCacheBuilding;
        private int moveMaskCacheCursor;
        private int moveMaskCacheRevision=-1;
        // Local hover/rules diagnostics. These counters are intentionally not
        // synchronized; they make cache warm-up and frame slicing observable
        // without changing authoritative Go state.
        public int moveMaskCachePoints;
        public int moveMaskCacheFrames;
        public int moveMaskCacheHits;
        public int moveMaskCacheMisses;
        public int maxMoveMaskPointsOneFrame;
        public float maxMoveMaskFrameMilliseconds;
        public bool moveMaskCacheActive;
        // After an AI move the visible board, audio and compact network
        // snapshot all settle in the same rendered frame. Defer the optional
        // hover-mask background scan briefly so that commit frame is not
        // followed by an avoidable CPU burst. GetMoveMask remains exact/lazy.
        [System.NonSerialized] public int moveMaskWarmupDeferredUntilFrame;
        private int pendingBoardRefreshReason=GoBoardView.REFRESH_OTHER;

        // Local evidence for the payload/rebuild gate. A false value blocks
        // rule queries and AI input rather than silently accepting a position
        // with incomplete superko history.
        public bool localHistoryRebuildFailed;
        // Last received snapshot identity. Configuration-only snapshots must
        // not cancel a valid SearchSession; position/turn changes must.
        private bool hasNetworkSnapshot;
        private int lastNetworkRevision=-1;
        private int lastNetworkMoveCount=-1;
        private int lastNetworkSideToMove=EMPTY;
        private int lastNetworkLastMove=NONE;

        public void Start()
        {
            pendingBoardRefreshReason=GoBoardView.REFRESH_INITIAL;
            if(aiSettings!=null)
            {
                aiSettings.game=this;
                aiSettings.blackDifficulty=blackDifficulty;
                aiSettings.whiteDifficulty=whiteDifficulty;
                settingsRevision=aiSettings.configRevision;
                aiSettings.RegisterProfiles(blackDifficulty,whiteDifficulty);
            }
            EnsureHistoryStoneCountStorage();
            EnsureMoveHistoryStorage();
            derivedHistoryReady=false;
            derivedHistoryRebuildFailed=false;
            localHistoryRebuildFailed=false;
            // Game state and telemetry are one authority domain. Reassert the
            // local owner's references before publishing the initial snapshot.
            if(IsLocalOwner())TakeOwnership();
            // A joining non-owner must wait for the authoritative snapshot; it
            // must never manufacture a local empty game before deserialization.
            // A generated scene can enter ClientSim with an owner already
            // assigned but with the legacy empty snapshot (moveCount=0,
            // positionHistoryCount=0). Establish the canonical initial
            // position before any starter/start event can launch AI; an
            // empty board still needs one positional-history entry.
            if(IsLocalOwner()&&(revision==0||
                (moveCount==0&&positionHistoryCount==0)))
            {
                ResetState(false);
                RequestSerialization();
            }
            // A non-owner may only consume the synchronized snapshot.  Do not
            // locally rewrite synchronized fields on a joining client; the
            // eventual owner performs the repair in RecoverNetworkState and
            // publishes one coherent snapshot.
            if(IsLocalOwner())SanitizeSynchronizedState();
            lastAudioEventRevision=audioEventRevision;
            hasNetworkSnapshot=true;
            lastNetworkRevision=revision;
            lastNetworkMoveCount=moveCount;
            lastNetworkSideToMove=sideToMove;
            lastNetworkLastMove=lastMove;
            Refresh();
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            serializationCount++;
            lastSerializationByteCount=result.byteCount;
            lastSerializationSuccess=result.success;
            if(!result.success)serializationFailureCount++;
        }

        public override void OnDeserialization()
        {
            if(aiController!=null)aiController.WakeForStateChange();
            pendingBoardRefreshReason=GoBoardView.REFRESH_DESERIALIZE;
            bool positionChanged=!hasNetworkSnapshot||revision!=lastNetworkRevision||
                moveCount!=lastNetworkMoveCount||sideToMove!=lastNetworkSideToMove||
                lastMove!=lastNetworkLastMove;
            hasNetworkSnapshot=true;
            lastNetworkRevision=revision;
            lastNetworkMoveCount=moveCount;
            lastNetworkSideToMove=sideToMove;
            lastNetworkLastMove=lastMove;
            // A configuration-only snapshot (for example a difficulty Apply)
            // keeps the immutable search root valid. Only a changed position,
            // turn or lifecycle invalidates pending GPU/search work.
            if(positionChanged&&aiController!=null)aiController.InvalidateSearch();
            if(positionChanged)
            {
                derivedHistoryReady=false;
                derivedHistoryRebuildFailed=false;
                localHistoryRebuildFailed=false;
                RebuildDerivedHistoryFromMoveLog();
            }
            // Keep the received synchronized state byte-for-byte intact on a
            // non-owner.  SanitizeSynchronizedState mutates synchronized
            // fields and is therefore reserved for the delayed owner recovery
            // path below.  This mirrors Xiangqi's snapshot-consumer lifecycle
            // and prevents a client from presenting a locally repaired state
            // that was never published by the authority.
            if(positionChanged)
            {
                if(view!=null)view.ClearHover();
                Refresh();
            }
            else
            {
                // Configuration-only snapshots do not need a 361-stone board
                // reconciliation. Refresh only the affected presentation
                // surfaces while the current search continues.
                if(ui!=null)ui.RefreshNow();
                if(telemetry!=null)telemetry.RefreshNow();
            }
            if(audioEventRevision!=lastAudioEventRevision){lastAudioEventRevision=audioEventRevision;PlayAudioEvent(audioEventType);}
            if(IsLocalOwner())SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // GoAiController is a sibling Udon behaviour on this gameplay object and
            // receives the same ownership event.  Its callback (plus the authority
            // transition check in Tick) owns the single ownerLifecycle increment; do not
            // forward a second cancellation from the game domain.
            if(Utilities.IsValid(player)&&player.isLocal)
                SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
            else
                Refresh();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            pendingBoardRefreshReason=GoBoardView.REFRESH_RECOVERY;
            if(IsLocalOwner())
            {
                bool changed=ClearSeatForIdentity(player==null?-1:player.playerId,player==null?"":player.displayName);
                if(SanitizeSynchronizedState())changed=true;
                if(changed){revision++;networkRecoveryRevision++;lastActionText="A player reconnected · stale Go seat released";RequestSerialization();}
            }
            Refresh();
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),2);
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),30);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            pendingBoardRefreshReason=GoBoardView.REFRESH_RECOVERY;
            if(IsLocalOwner())
            {
                bool changed=ClearSeatForIdentity(player==null?-1:player.playerId,player==null?"":player.displayName);
                if(ClearAIControlAuthorityForIdentity(player==null?-1:player.playerId,player==null?"":player.displayName))changed=true;
                if(SanitizeSynchronizedState())changed=true;
                if(changed){revision++;networkRecoveryRevision++;lastActionText="A player left · Go seat released";RequestSerialization();}
            }
            Refresh();
            // Ownership transfer and OnPlayerLeft ordering is not guaranteed;
            // every client schedules the same repair and only the eventual
            // owner is allowed to mutate synchronized state.
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),4);
            SendCustomEventDelayedFrames(nameof(RecoverNetworkState),30);
        }
        public void RecoverNetworkState()
        {
            pendingBoardRefreshReason=GoBoardView.REFRESH_RECOVERY;
            EnsureHistoryStoneCountStorage();
            EnsureMoveHistoryStorage();
            if(!IsLocalOwner()){Refresh();return;}
            if(!TakeOwnership()){Refresh();return;}
            // Clamp malformed synchronized scalar/controller fields before
            // replaying the compact rule log. Otherwise an invalid move or
            // history count would prevent the owner from ever reaching the
            // sanitizer that is responsible for repairing it.
            bool changed=SanitizeSynchronizedState();
            if(!EnsureDerivedHistory())
            {
                Refresh();
                return;
            }
            if(changed)
            {
                revision++;
                networkRecoveryRevision++;
                lastActionText="Network recovery complete · stale Go state repaired";
                RequestSerialization();
            }
            Refresh();
        }
        public void NewGame(){RequestNewGame();}

        private void ResetState(bool serialize)
        {
            pendingBoardRefreshReason=GoBoardView.REFRESH_RESET;
            derivedHistoryReady=false;
            derivedHistoryRebuildFailed=false;
            localHistoryRebuildFailed=false;
            EnsureHistoryStoneCountStorage();
            EnsureMoveHistoryStorage();
            for(int i=0;i<AREA;i++){ board[i]=EMPTY; previousBoard1[i]=EMPTY; previousBoard2[i]=EMPTY; }
            for(int i=0;i<5;i++){ recentMoveLoc[i]=NONE; recentMovePla[i]=EMPTY; }
            for(int i=0;i<MAX_POSITION_HISTORY;i++){ hash0[i]=0; hash1[i]=0; hash2[i]=0; hash3[i]=0; historyStoneCount[i]=-1; }
            for(int i=0;i<MAX_MOVES;i++)moveHistory[i]=0;
            historyStoneCountVersion=1;historyStoneCountFrequencyValid=false;
            positionHistoryCount=0; if(starter!=BLACK&&starter!=WHITE)starter=BLACK; sideToMove=starter; moveCount=0; consecutivePasses=0; blackCaptures=0; whiteCaptures=0;
            gameState=STATE_PLAYING; winner=EMPTY; lastMove=NONE; koLoc=NONE; handicapStones=0;
            turnStartedServerSecond=0;
            moveMaskWarmupDeferredUntilFrame=0;
            hintMove=NONE; hintRevision++; aiHintsEnabled=false; aiHintPermissionLocked=false;
            aiHintPermissionRevision++; ClearPendingOffers(); nextLocalDrawRequestTime=0f;
            if(telemetry!=null)telemetry.ClearFinalAnalysis();
            // Every mode has an explicit synchronized start gate.  This
            // keeps the PvP seats claimable and gives AI modes a real
            // pre-match hint-permission window.
            matchStarted=false; finalBlackArea=0; finalWhiteArea=0; finalWhiteMinusBlackScore=0f; lastActionText="New game · press Start Match";
            ComputeHash(); AppendHash(); revision++;
            MarkDerivedHistoryReady();
            if(serialize){PublishAudio(AUDIO_RESET);RequestSerialization();} Refresh();
        }

        public bool TryPlay(int loc){return TryPlayInternal(loc,false);}
        public bool TryPlayFromAI(int loc){return TryPlayInternal(loc,true);}

        /// <summary>
        /// Replays a fixed verifier fixture through the exact authoritative
        /// move path while coalescing setup-only presentation/network work.
        /// Every move still executes TryPlayInternal/PassInternal, including
        /// capture, suicide, ko and positional-superko checks.  This is only
        /// for deterministic offline ClientSim setup; production callers use
        /// TryPlayFromAI/PassFromAI and retain per-move publication.
        /// </summary>
        public bool ReplayMovesForVerifier(int[] moves,int offset,int count)
        {
            if(moves==null||offset<0||count<1||offset+count>moves.Length)return false;
            bool previous=suppressReplayPublication;
            suppressReplayPublication=true;
            for(int i=0;i<count;i++)
            {
                int loc=moves[offset+i];
                int before=moveCount;
                if(loc==PASS)PassInternal(true);
                else TryPlayInternal(loc,true);
                if(moveCount!=before+1)
                {
                    suppressReplayPublication=previous;
                    return false;
                }
            }
            suppressReplayPublication=previous;
            Refresh();
            return true;
        }

        private bool TryPlayInternal(int loc,bool fromAI)
        {
            if(!CanAttempt(loc,fromAI)) return false;
            if(!EnsureDerivedHistory())
            {
                SetActionMessage("Play denied · Go move history is unavailable");
                return false;
            }
            if(!fromAI&&!EnsureLocalSeat(sideToMove)) return false;
            if(fromAI&&!IsLocalOwner()) return false;
            if(!TakeOwnership()) { lastActionText="Play denied · authoritative Go owner unavailable"; return false; }
            int pla=sideToMove; Copy(board,backup); int oldB=blackCaptures, oldW=whiteCaptures, oldKo=koLoc;
            board[loc]=pla; int captured=0, single=NONE;
            int n=Left(loc); if(n>=0&&board[n]==-pla) captured+=CaptureDead(n,-pla,ref single);
            n=Right(loc); if(n>=0&&board[n]==-pla) captured+=CaptureDead(n,-pla,ref single);
            n=Up(loc); if(n>=0&&board[n]==-pla) captured+=CaptureDead(n,-pla,ref single);
            n=Down(loc); if(n>=0&&board[n]==-pla) captured+=CaptureDead(n,-pla,ref single);
            int ownSize=CollectGroup(loc,pla); int ownLibs=CountLiberties(); int selfRemoved=0;
            if(ownLibs==0){ if(ownSize==1||!multiStoneSuicideLegal){ Reject(oldB,oldW,oldKo); return false; } selfRemoved=ownSize; for(int i=0;i<ownSize;i++) board[group[i]]=EMPTY; }
            ComputeHash(); if(positionalSuperko&&HashSeen(h0,h1,h2,h3)){ Reject(oldB,oldW,oldKo); return false; }
            Copy(previousBoard1,previousBoard2); Copy(backup,previousBoard1);
            if(pla==BLACK){ blackCaptures+=captured; whiteCaptures+=selfRemoved; }
            else{ whiteCaptures+=captured; blackCaptures+=selfRemoved; }
            koLoc=NONE;
            if(captured==1&&selfRemoved==0&&board[loc]==pla){ int sz=CollectGroup(loc,pla); if(sz==1&&CountLiberties()==1) koLoc=single; }
            PushRecentMove(pla,loc); sideToMove=-pla; consecutivePasses=0; lastMove=loc; RecordMove(pla,loc); moveCount++; AppendHash(); ClearPendingOffers(); revision++;
            hintMove=NONE; hintRevision++;
            lastActionText=(pla==BLACK?"Black ":"White ")+Coordinate(loc)+(captured>0?" · capture "+captured:"");
            bool reachedCapacity=IsAtCapacity();
            if(reachedCapacity)FinishByMoveLimit();
            StampTurnTimer();
            if(fromAI)moveMaskWarmupDeferredUntilFrame=Time.frameCount+4;
            if(aiController!=null&&!suppressReplayPublication)aiController.InvalidateSearch();
            MarkDerivedHistoryReady();
            pendingBoardRefreshReason=captured>0?GoBoardView.REFRESH_CAPTURE:
                GoBoardView.REFRESH_MOVE;
            if(!suppressReplayPublication)
            {
                PublishAudio(reachedCapacity?AUDIO_GAME_END:(captured>0?AUDIO_CAPTURE:AUDIO_PLACE));
                Refresh(); RequestSerialization();
            }
            return true;
        }

        public bool IsLegalMove(int loc)
        {
            if(!CanAttempt(loc,false)) return false;
            return IsRulesLegalMove(loc);
        }

        /// <summary>
        /// Pure rules query. Unlike IsLegalMove, this does not inspect match
        /// seats or whether the side is controlled by a human/AI. It is the
        /// correct API for validators, hints, replay, and external callers
        /// asking whether a Go move (including PASS) is legal in this state.
        /// </summary>
        public bool IsRulesLegalMove(int loc)
        {
            if(!EnsureDerivedHistory())return false;
            if(loc==PASS)
                return gameState==STATE_PLAYING&&moveCount<MAX_MOVES&&positionHistoryCount<MAX_POSITION_HISTORY;
            return (GetMoveMask(loc)&1)!=0;
        }
        public bool IsSuperkoBanned(int loc)
        {
            if(!EnsureDerivedHistory())return false;
            return (GetMoveMask(loc)&2)!=0;
        }

        /// <summary>
        /// Computes ordinary legality and the separate superko mask once for
        /// the current authoritative position. Board hover, hint validation,
        /// and AI-facing rule probes can share this exact result instead of
        /// repeating a full 19x19 group/hash simulation.
        /// </summary>
        public int GetMoveMask(int loc)
        {
            if(!EnsureDerivedHistory()||loc<0||loc>=AREA)return 0;
            EnsureMoveMaskCache();
            if(moveMaskComputedGeneration[loc]==moveMaskCacheGeneration)
            {
                moveMaskCacheHits++;
                return moveMaskCache[loc];
            }
            moveMaskCacheMisses++;
            int mask=0;
            if(CanRulesAttempt(loc))
            {
                int pla=sideToMove;int oldKo=koLoc;Copy(board,backup);board[loc]=pla;int ignored=NONE;
                int n=Left(loc);if(n>=0&&board[n]==-pla)CaptureDead(n,-pla,ref ignored);
                n=Right(loc);if(n>=0&&board[n]==-pla)CaptureDead(n,-pla,ref ignored);
                n=Up(loc);if(n>=0&&board[n]==-pla)CaptureDead(n,-pla,ref ignored);
                n=Down(loc);if(n>=0&&board[n]==-pla)CaptureDead(n,-pla,ref ignored);
                int size=CollectGroup(loc,pla);bool ordinaryLegal=true;
                if(CountLiberties()==0)
                {
                    if(size==1||!multiStoneSuicideLegal)ordinaryLegal=false;
                    else
                    {
                        for(int i=0;i<size;i++)board[group[i]]=EMPTY;
                    }
                }
                bool superkoBanned=false;
                if(ordinaryLegal&&positionalSuperko&&loc!=oldKo)
                {
                    // Captures and legal multi-stone suicide alter the stone
                    // count; derive the exact count from the temporary board
                    // before restoring it.
                    ComputeHash();int candidateStoneCount=currentStoneCount;
                    superkoBanned=HistoryMayContainStoneCount(candidateStoneCount);
                    if(superkoBanned)superkoBanned=HashSeen(h0,h1,h2,h3);
                }
                if(ordinaryLegal&&!superkoBanned)mask|=1;
                if(superkoBanned)mask|=2;
                Copy(backup,board);koLoc=oldKo;ComputeHash();
            }
            moveMaskCache[loc]=mask;
            moveMaskComputedGeneration[loc]=moveMaskCacheGeneration;
            return mask;
        }
        private bool CanAttempt(int loc,bool fromAI){ return matchStarted&&CanRulesAttempt(loc)&&(fromAI?IsAIControlled(sideToMove):!IsAIControlled(sideToMove)); }
        private bool CanRulesAttempt(int loc){ return gameState==STATE_PLAYING&&moveCount<MAX_MOVES&&positionHistoryCount<MAX_POSITION_HISTORY&&loc>=0&&loc<AREA&&board[loc]==EMPTY&&loc!=koLoc; }
        private bool IsAtCapacity(){ return moveCount>=MAX_MOVES||positionHistoryCount>=MAX_POSITION_HISTORY; }

        public void Pass(){PassInternal(false);}
        public void PassFromAI(){PassInternal(true);}
        private void PassInternal(bool fromAI)
        {
            if(!matchStarted||gameState!=STATE_PLAYING||fromAI!=IsAIControlled(sideToMove)) return;
            if(!EnsureDerivedHistory()){SetActionMessage("Pass denied · Go move history is unavailable");return;}
            if(!fromAI&&!EnsureLocalSeat(sideToMove))return;
            if(fromAI&&!IsLocalOwner())return;
            if(!TakeOwnership()){SetActionMessage("Pass denied · authoritative Go owner unavailable");return;}
            if(IsAtCapacity()){FinishByMoveLimit();revision++;if(aiController!=null&&!suppressReplayPublication)aiController.InvalidateSearch();if(!suppressReplayPublication){PublishAudio(AUDIO_GAME_END);Refresh();RequestSerialization();}return;}
            int pla=sideToMove; Copy(previousBoard1,previousBoard2); Copy(board,previousBoard1); PushRecentMove(pla,PASS);
            sideToMove=-pla; consecutivePasses++; lastMove=PASS; koLoc=NONE; RecordMove(pla,PASS); moveCount++; ComputeHash(); AppendHash(); ClearPendingOffers(); revision++; hintMove=NONE; hintRevision++; lastActionText="Pass";
            StampTurnTimer();
            if(aiController!=null&&!suppressReplayPublication)aiController.InvalidateSearch();
            MarkDerivedHistoryReady();
            pendingBoardRefreshReason=GoBoardView.REFRESH_MOVE;
            if(!suppressReplayPublication)
            {
                if(consecutivePasses>=2){FinishByScore();PublishAudio(AUDIO_GAME_END);}else if(IsAtCapacity()){FinishByMoveLimit();PublishAudio(AUDIO_GAME_END);}else PublishAudio(AUDIO_PASS);
                Refresh(); RequestSerialization();
            }
        }
        public void Resign(){ResignInternal(false);}
        public void ResignFromAI(){ResignInternal(true);}
        private void ResignInternal(bool fromAI){ if(!matchStarted||gameState!=STATE_PLAYING||fromAI!=IsAIControlled(sideToMove))return; if(!fromAI&&!EnsureLocalSeat(sideToMove))return; if(fromAI&&!IsLocalOwner())return; if(!TakeOwnership()){SetActionMessage("Resign denied · authoritative Go owner unavailable");return;} int resigned=sideToMove; winner=-resigned; gameState=STATE_RESIGNED; turnStartedServerSecond=0; ClearPendingOffers(); hintMove=NONE; hintRevision++; revision++; lastActionText=resigned==BLACK?"Black resigned":"White resigned"; if(aiController!=null)aiController.InvalidateSearch(); PublishAudio(AUDIO_GAME_END); Refresh(); RequestSerialization(); }

        public void ResignLocal(){Resign();}

        /// <summary>
        /// PvP uses a synchronized consent request.  PvAI/AIvP rolls back to
        /// the previous human decision point, matching the Xiangqi product
        /// behaviour while rebuilding Go's complete positional history.
        /// </summary>
        public void RequestUndo()
        {
            if(moveCount<=0){SetActionMessage("There is no Go move to undo");return;}
            if(!CanLocalUndo())
            {
                if(GetAIControlCount()==0&&GetLocalSeatSide()!=EMPTY)
                    SetActionMessage("Undo requires the current Go player; the other player must accept");
                else
                    SetActionMessage("Undo requires a seated Go player; spectators cannot change this table");
                return;
            }
            int localSide=GetLocalSeatSide();
            if(GetAIControlCount()==0)
            {
                if(localSide==EMPTY){SetActionMessage("Claim the Black or White seat first");return;}
                if(!TakeLocalMatchOwnership()){SetActionMessage("Undo denied · authoritative Go owner unavailable");return;}
                if(undoOfferSide==-localSide)
                {
                    if(!IsOfferCurrent(undoOfferSide,undoOfferRevision,undoOfferMoveCount))
                    {
                        ClearPendingOffers();revision++;lastActionText="Undo request expired";Refresh();RequestSerialization();return;
                    }
                    CommitUndo(moveCount-1,1);return;
                }
                if(undoOfferSide==localSide)
                {
                    if(!IsOfferCurrent(undoOfferSide,undoOfferRevision,undoOfferMoveCount))
                    {
                        ClearPendingUndo();revision++;Refresh();RequestSerialization();
                    }
                    else SetActionMessage("Your undo request is waiting for the other player");
                    return;
                }
                ClearPendingOffers();revision++;
                undoOfferSide=localSide;undoOfferRevision=revision;undoOfferMoveCount=moveCount;
                lastActionText=(localSide==BLACK?"Black":"White")+" requested undo · waiting for opponent";
                Refresh();RequestSerialization();return;
            }

            if(!TakeLocalMatchOwnership()){SetActionMessage("Undo denied · authoritative Go owner unavailable");return;}
            int undoCount=1;
            if(GetAIControlCount()==1&&!IsAIControlled(sideToMove)&&IsAIControlled(-sideToMove)&&moveCount>=2)undoCount=2;
            CommitUndo(moveCount-undoCount,undoCount);
        }

        /// <summary>Offers a draw in PvP and accepts the opponent's offer.</summary>
        public void OfferOrAcceptDraw()
        {
            if(gameState!=STATE_PLAYING||!matchStarted){SetActionMessage("Start a Go match before requesting a draw");return;}
            if(GetAIControlCount()!=0){SetActionMessage("Draw consent requires two seated players");return;}
            if(!RequireLocalMatchControl("Draw",false))return;
            int localSide=GetLocalSeatSide();
            if(localSide==EMPTY){SetActionMessage("Claim the Black or White seat first");return;}
            if(GetDrawCooldownSeconds()>0){SetActionMessage("Draw cooldown · wait "+GetDrawCooldownSeconds()+" seconds");return;}
            nextLocalDrawRequestTime=Time.realtimeSinceStartup+5f;
            if(!TakeLocalMatchOwnership()){SetActionMessage("Draw denied · authoritative Go owner unavailable");return;}
            if(drawOfferSide==-localSide)
            {
                if(!IsOfferCurrent(drawOfferSide,drawOfferRevision,drawOfferMoveCount))
                {
                    ClearPendingOffers();revision++;lastActionText="Draw request expired";Refresh();RequestSerialization();return;
                }
                drawOfferSide=EMPTY;drawOfferRevision=0;drawOfferMoveCount=-1;
                gameState=STATE_DRAW;winner=EMPTY;ClearPendingUndo();hintMove=NONE;hintRevision++;revision++;
                lastActionText="Both players accepted a draw";PublishAudio(AUDIO_GAME_END);Refresh();RequestSerialization();return;
            }
            if(drawOfferSide==localSide){SetActionMessage("Your draw offer is waiting for the other player");return;}
            ClearPendingDraw();revision++;
            drawOfferSide=localSide;drawOfferRevision=revision;drawOfferMoveCount=moveCount;
            lastActionText=(localSide==BLACK?"Black":"White")+" offered a draw · waiting for opponent";
            Refresh();RequestSerialization();
        }

        public int GetDrawCooldownSeconds()
        {
            float remaining=nextLocalDrawRequestTime-Time.realtimeSinceStartup;
            return remaining>0f?Mathf.CeilToInt(remaining):0;
        }

        public string GetUndoActionLabel(bool english)
        {
            int localSide=GetLocalSeatSide();
            if(GetAIControlCount()==0&&localSide!=EMPTY&&undoOfferSide==-localSide)return english?"ACCEPT UNDO":"同意悔棋";
            return GetAIControlCount()==0?(english?"REQUEST UNDO":"请求悔棋"):(english?"UNDO":"悔棋");
        }

        public string GetDrawActionLabel(bool english)
        {
            if(GetAIControlCount()!=0)return english?"DRAW":"和棋";
            int localSide=GetLocalSeatSide();
            if(localSide!=EMPTY&&drawOfferSide==-localSide)return english?"ACCEPT DRAW":"同意和棋";
            return english?"OFFER DRAW":"提出和棋";
        }

        public void RequestAiHint()
        {
            if (aiController == null)
            {
                SetActionMessage("AI hint unavailable · controller is not bound");
                return;
            }
            if (!matchStarted || gameState != STATE_PLAYING)
            {
                SetActionMessage("Start a Go match before requesting a hint");
                return;
            }
            if(!aiHintsEnabled)
            {
                SetActionMessage("AI hint is locked off · enable it before starting this match");
                return;
            }
            if(!aiHintPermissionLocked)
            {
                SetActionMessage("Confirm AI hint permission before starting this match");
                return;
            }
            if (IsAIControlled(sideToMove))
            {
                SetActionMessage("The current Go side is AI-controlled");
                return;
            }
            if (!RequireLocalMatchControl("AI hint", false)) return;
            if (!TakeLocalMatchOwnership())
            {
                SetActionMessage("AI hint denied · authoritative Go owner unavailable");
                return;
            }
            aiController.InvalidateSearch();
            hintMove=NONE; hintRevision++; revision++;
            lastActionText="AI hint requested · searching without auto-play";
            Refresh(); RequestSerialization();
            aiController.BeginHintSearch();
        }

        /// <summary>
        /// Pre-match AI hint consent.  It is deliberately separate from the
        /// request button: after Start Match this state is frozen for the
        /// complete game and every client receives the same result.
        /// </summary>
        public void ToggleAiHintPermission()
        {
            if(matchStarted||aiHintPermissionLocked)
            {
                SetActionMessage(aiHintsEnabled
                    ?"AI hint permission is locked on for this match"
                    :"AI hint permission is locked off for this match");
                return;
            }
            if(!CanLocalMatchControl())
            {
                SetActionMessage("Only the current Go controller can confirm AI hint permission");
                return;
            }
            if(!TakeLocalMatchOwnership())
            {
                SetActionMessage("AI hint permission denied · authoritative Go owner unavailable");
                return;
            }
            aiHintsEnabled=!aiHintsEnabled;
            aiHintPermissionRevision++;
            if(aiHintPermissionRevision==0)aiHintPermissionRevision=1;
            revision++;
            lastActionText=aiHintsEnabled
                ?"AI hint enabled · press Start Match to lock"
                :"AI hint disabled · press Start Match to lock";
            Refresh();RequestSerialization();
        }

        public string GetAiHintPermissionLabel(bool english)
        {
            if(aiHintPermissionLocked)
                return aiHintsEnabled
                    ?(english?"AI HINT · ON · LOCKED":"AI 提示 · 开启 · 已锁定")
                    :(english?"AI HINT · OFF · LOCKED":"AI 提示 · 关闭 · 已锁定");
            return aiHintsEnabled
                ?(english?"AI HINT · ON · CONFIRM":"AI 提示 · 开启 · 待确认")
                :(english?"AI HINT · OFF · TAP TO ENABLE":"AI 提示 · 关闭 · 开局前开启");
        }

        public void ClearPublishedAiHint()
        {
            if (hintMove == NONE) return;
            if (!TakeOwnership()) return;
            hintMove=NONE; hintRevision++; revision++;
            lastActionText="AI hint cleared";
            Refresh(); RequestSerialization();
        }

        public void PublishAiHint(int move)
        {
            if (!IsLocalOwner()) return;
            if (gameState != STATE_PLAYING || !matchStarted)
            {
                hintMove=NONE;
            }
            else if (move != PASS && (move < 0 || move >= AREA || !IsRulesLegalMove(move)))
            {
                hintMove=NONE;
            }
            else
            {
                hintMove=move;
            }
            hintRevision++;
            revision++;
            lastActionText=hintMove==PASS ? "AI hint · pass" :
                (hintMove>=0 ? "AI hint · "+Coordinate(hintMove) : "AI hint found no legal move");
            Refresh(); RequestSerialization();
        }

        public bool SetHandicap(int count)
        {
            // Handicap placement rewrites the authoritative position just
            // like New Game. Keep it behind the same match-control boundary
            // so a spectator cannot call the public endpoint and erase a
            // player's opening position. During a live match only the
            // existing force-reset authorities may do so.
            if(moveCount!=0||gameState!=STATE_PLAYING||count<2||count>9)return false;
            if(matchStarted&&!CanLocalForceReset())return false;
            if(!CanLocalMatchControl())return false;
            if(!TakeOwnership())return false;
            if(aiController!=null)aiController.InvalidateSearch();
            EnsureMoveHistoryStorage();
            for(int i=0;i<AREA;i++) board[i]=EMPTY;
            for(int i=0;i<MAX_MOVES;i++)moveHistory[i]=0;
            PlaceHandicapStones(count);
            Copy(board,previousBoard1); Copy(board,previousBoard2); for(int i=0;i<MAX_POSITION_HISTORY;i++){hash0[i]=0;hash1[i]=0;hash2[i]=0;hash3[i]=0;historyStoneCount[i]=-1;}
            historyStoneCountVersion=1;historyStoneCountFrequencyValid=false;
            for(int i=0;i<5;i++){recentMoveLoc[i]=NONE;recentMovePla[i]=EMPTY;}
            blackCaptures=0; whiteCaptures=0; finalBlackArea=0; finalWhiteArea=0; finalWhiteMinusBlackScore=0f;
            positionHistoryCount=0; ComputeHash(); AppendHash(); handicapStones=count; sideToMove=WHITE; consecutivePasses=0; koLoc=NONE; lastMove=NONE; gameState=STATE_PLAYING; winner=EMPTY; turnStartedServerSecond=0; ClearPendingOffers(); hintMove=NONE; hintRevision++;
            revision++; MarkDerivedHistoryReady(); lastActionText="Handicap "+count;
            pendingBoardRefreshReason=GoBoardView.REFRESH_HANDICAP;
            PublishAudio(AUDIO_RESET); Refresh(); RequestSerialization(); return true;
        }

        public bool IsAdaptiveMode(){return HasAnyAI();}
        public bool RequiresManualStart(){return HasAnyAI();}

        public bool IsAIControlled(int side)
        {
            return side==BLACK?blackIsAI:side==WHITE&&whiteIsAI;
        }

        public bool HasAnyAI(){return blackIsAI||whiteIsAI;}
        public int GetAIControlCount(){return (blackIsAI?1:0)+(whiteIsAI?1:0);}
        public int GetAISide()
        {
            if(blackIsAI&&whiteIsAI)return sideToMove;
            if(blackIsAI)return BLACK;
            if(whiteIsAI)return WHITE;
            return EMPTY;
        }

        public string GetMatchModeName(bool english)
        {
            int count=GetAIControlCount();
            if(count==0)return english?"Player vs Player":"玩家 vs 玩家";
            if(count==2)return english?"AI vs AI":"AI vs AI";
            return english?"Player vs AI":"玩家 vs AI";
        }

        public string GetStatusLine(){return GetStatusLineLocalized(false);}
        public string GetStatusLineLocalized(bool english)
        {
            if(gameState==STATE_BLACK_WINS)return english?"Black wins · Match complete":"黑方胜 · 对局结束";
            if(gameState==STATE_WHITE_WINS)return english?"White wins · Match complete":"白方胜 · 对局结束";
            if(gameState==STATE_DRAW)return english?"Draw · Match complete":"和棋 · 对局结束";
            string mode=GetMatchModeName(english);
            if(!matchStarted)return english?mode+" · Ready to start":mode+" · 等待开始";
            string side=sideToMove==BLACK?(english?"Black to move":"黑方行棋"):(english?"White to move":"白方行棋");
            return mode+" · "+side+" · "+(english?"Move ":"第 ")+(moveCount+1)+(english?"":" 手");
        }

        public string GetSeatLine(){return GetSeatLineLocalized(false);}
        public string GetSeatLineLocalized(bool english)
        {
            string black=blackIsAI?(english?"AI":"AI"):(blackPlayerName==null||blackPlayerName.Length==0?(english?"Open":"空位"):blackPlayerName);
            string white=whiteIsAI?(english?"AI":"AI"):(whitePlayerName==null||whitePlayerName.Length==0?(english?"Open":"空位"):whitePlayerName);
            return english?"Black "+black+"  |  White "+white:"黑方 "+black+"  |  白方 "+white;
        }

        public string GetStarterLineLocalized(bool english)
        {
            return english
                ? (starter==BLACK?"BLACK STARTS":"WHITE STARTS")
                : (starter==BLACK?"黑方先手":"白方先手");
        }

        public string GetControllerLineLocalized(bool english)
        {
            string black=blackIsAI?(english?"AI":"AI"):(english?"PLAYER":"玩家");
            string white=whiteIsAI?(english?"AI":"AI"):(english?"PLAYER":"玩家");
            return english?"BLACK · "+black+"  /  WHITE · "+white:"黑方 · "+black+"  /  白方 · "+white;
        }

        public bool HasStaleSeat()
        {
            return (!blackIsAI&&blackPlayerId>=0&&!IsSeatConnected(blackPlayerId,blackPlayerName))||
                (!whiteIsAI&&whitePlayerId>=0&&!IsSeatConnected(whitePlayerId,whitePlayerName));
        }

        public bool HasVerifiedPositionHistory()
        {
            return EnsureDerivedHistory();
        }

        public string GetNetworkHealthLocalized(bool english)
        {
            if(localHistoryRebuildFailed)
                return english?"RECOVERY BLOCKED · complete Go move history was not verified":
                    "恢复已阻止 · 未能验证完整围棋手数历史";
            if(HasStaleSeat())
                return english?"RECOVERY PENDING · stale Go seat":"等待网络恢复 · 检测到失效围棋席位";
            return english?"SYNCED":"同步正常";
        }

        public void RequestNewGame()
        {
            // During a live game, New Game is a destructive reset.  Keep it
            // administrative just like Force Reset so a seated opponent
            // cannot erase the table while the other player is thinking.  A
            // completed game can still be restarted by either seated player.
            if(matchStarted&&gameState==STATE_PLAYING&&!CanLocalForceReset())
            {
                SetActionMessage("New game is locked during a live match; use Force Reset as table admin");
                return;
            }
            if(!RequireLocalMatchControl("Reset game",true))return;
            if(!TakeLocalMatchOwnership()){SetActionMessage("Reset denied · authoritative Go owner unavailable");return;}
            if(aiController!=null)aiController.InvalidateSearch();
            ResetState(false);
            lastActionText=HasAnyAI()?"New game · press Start Match":"New game";
            pendingBoardRefreshReason=GoBoardView.REFRESH_RESET;
            PublishAudio(AUDIO_RESET);Refresh();RequestSerialization();
        }

        public void RequestStartMatch()
        {
            if(!HasAnyAI())
            {
                if(matchStarted)return;
                if(!CanLocalMatchControl())
                {
                    SetActionMessage("Start Match is limited to a Go seat holder, an empty table, or instance master");
                    return;
                }
                if(!TakeLocalMatchOwnership())
                {
                    SetActionMessage("Start Match denied · authoritative Go owner unavailable");
                    return;
                }
                aiHintPermissionLocked=true;aiHintPermissionRevision++;
                matchStarted=true;StampTurnTimer();revision++;lastActionText="Match started · Player vs Player";
                Refresh();RequestSerialization();return;
            }
            if(matchStarted){SetActionMessage("Match already started");return;}
            if(gameState!=STATE_PLAYING){SetActionMessage("Create a new game first");return;}
            if(!CanLocalMatchControl())
            {
                SetActionMessage(GetAIControlCount()==2
                    ?"Start Match is limited to the AI-AI controller, state owner, or instance master"
                    :"Start Match is limited to a seat holder, an empty table, or instance master");
                return;
            }
            if(!TakeLocalMatchOwnership()){SetActionMessage("Start Match denied · authoritative Go owner unavailable");return;}
            if(aiController!=null)aiController.InvalidateSearch();
            aiHintPermissionLocked=true;aiHintPermissionRevision++;
            matchStarted=true;StampTurnTimer();revision++;lastActionText="Match started · "+GetMatchModeName(true);
            if(aiController!=null)aiController.RequestAiResourceWarmup();
            Refresh();RequestSerialization();
        }

        public void RequestForceReset()
        {
            if(!CanLocalForceReset())
            {
                SetActionMessage("Force reset denied · another player's Go seat is active");
                return;
            }
            if(!TakeLocalMatchOwnership()){SetActionMessage("Force reset denied · authoritative Go owner unavailable");return;}
            if(aiController!=null)aiController.InvalidateSearch();
            ResetState(false);networkRecoveryRevision++;lastActionText=HasAnyAI()?"Force reset · press Start Match":"Force reset";
            pendingBoardRefreshReason=GoBoardView.REFRESH_RESET;
            PublishAudio(AUDIO_RESET);Refresh();RequestSerialization();
        }

        public void SetStarter(int requested)
        {
            if(requested!=BLACK&&requested!=WHITE)return;
            if(starter==requested){SetActionMessage(GetStarterLineLocalized(true));return;}
            if(moveCount>0){SetActionMessage("Choose the first player before the first move");return;}
            if(matchStarted&&gameState==STATE_PLAYING&&!CanLocalForceReset())
            {
                SetActionMessage("Starter changes are locked during a live match");
                return;
            }
            if(!CanLocalMatchControl()){SetActionMessage("Only a seat holder, table controller, or instance master can choose first player");return;}
            if(!TakeLocalMatchOwnership()){SetActionMessage("Starter change denied · authoritative Go owner unavailable");return;}
            if(moveCount==0&&positionHistoryCount==0)ResetState(false);
            if(aiController!=null)aiController.InvalidateSearch();
            starter=requested;sideToMove=starter;ClearPendingOffers();revision++;lastActionText=GetStarterLineLocalized(true)+" · ready";Refresh();RequestSerialization();
        }

        public void SetBlackStarts(){SetStarter(BLACK);}
        public void SetWhiteStarts(){SetStarter(WHITE);}
        public void ToggleStarter(){SetStarter(starter==BLACK?WHITE:BLACK);}

        public void NotifyAIProfileChanged(int side)
        {
            if(aiSettings!=null)
            {
                NotifyAISettingsApplied(aiSettings.configRevision);
                return;
            }
            if(!IsAIControlled(side))return;
            if(!TakeLocalMatchOwnership())return;
            settingsRevision++;if(settingsRevision==0)settingsRevision=1;
            hintMove=NONE;hintRevision++;
            // Configuration revisions are separate from the Go position
            // revision. A profile update must not invalidate the immutable
            // SearchSession already evaluating this root; the updated profile
            // is consumed by the next BeginSearch call.
            lastActionText=(side==BLACK?"Black":"White")+" AI profile synchronized";
            Refresh();RequestSerialization();
        }

        /// <summary>
        /// Mirrors the single synchronized settings-domain revision locally.
        /// This is presentation/search metadata only and never changes the Go
        /// position revision or serializes the game object.
        /// </summary>
        public void NotifyAISettingsApplied(int appliedRevision)
        {
            settingsRevision=appliedRevision<0?0:appliedRevision;
            if(ui!=null)ui.RefreshNow();
            if(telemetry!=null)telemetry.RefreshNow();
        }

        public void SetMatchMode(int requested)
        {
            if(requested<0||requested>3)requested=1;
            if(requested==0)SetControllerConfiguration(false,false,"Player vs Player");
            else if(requested==1)SetControllerConfiguration(false,true,"Player vs AI");
            else if(requested==2)SetControllerConfiguration(true,false,"AI vs Player");
            else SetControllerConfiguration(true,true,"AI vs AI");
        }

        public void SetAiSide(int side)
        {
            if(side==BLACK)SetControllerConfiguration(true,false,"Black AI");
            else if(side==WHITE)SetControllerConfiguration(false,true,"White AI");
        }

        public void SwitchAdaptiveToAI(){SetMatchMode(1);}
        public void SwitchAdaptiveToPVP(){SetMatchMode(0);}
        public void ToggleBlackController(){SetControllerConfiguration(!blackIsAI,whiteIsAI,blackIsAI?"Black player":"Black AI");}
        public void ToggleWhiteController(){SetControllerConfiguration(blackIsAI,!whiteIsAI,whiteIsAI?"White player":"White AI");}

        private void SetControllerConfiguration(bool useBlackAI,bool useWhiteAI,string action)
        {
            if(blackIsAI==useBlackAI&&whiteIsAI==useWhiteAI){SetActionMessage("Controller configuration unchanged");return;}
            if(matchStarted&&gameState==STATE_PLAYING&&!CanLocalForceReset())
            {
                SetActionMessage("Controller changes are locked during a live match");
                return;
            }
            if(!CanLocalMatchControl())
            {
                SetActionMessage(GetAIControlCount()==2
                    ?"Controller changes are limited to the AI-AI controller, state owner, or instance master"
                    :"Controller changes are limited to a seat holder, an empty table, or instance master");
                return;
            }
            int localId=LocalPlayerId();string localName=LocalPlayerName();bool hasLocal=Utilities.IsValid(Networking.LocalPlayer);
            if(hasLocal&&useBlackAI&&!blackIsAI&&IsSeatConnected(blackPlayerId,blackPlayerName)&&!SeatIdentityMatches(blackPlayerId,blackPlayerName,localId,localName)&&!Networking.IsMaster){SetActionMessage("Black player is still online");return;}
            if(hasLocal&&useWhiteAI&&!whiteIsAI&&IsSeatConnected(whitePlayerId,whitePlayerName)&&!SeatIdentityMatches(whitePlayerId,whitePlayerName,localId,localName)&&!Networking.IsMaster){SetActionMessage("White player is still online");return;}
            if(!TakeLocalMatchOwnership()){SetActionMessage("Controller change denied · authoritative Go owner unavailable");return;}
            if(aiController!=null)aiController.InvalidateSearch();
            blackIsAI=useBlackAI;whiteIsAI=useWhiteAI;
            if(blackIsAI){blackPlayerId=-1;blackPlayerName="";}
            if(whiteIsAI){whitePlayerId=-1;whitePlayerName="";}
            if(blackIsAI&&whiteIsAI&&hasLocal)
            {
                aiControlAuthorityPlayerId=localId;aiControlAuthorityName=localName;
            }
            else
            {
                aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";
            }
            if(blackIsAI!=whiteIsAI)humanSide=blackIsAI?WHITE:BLACK;
            ResetState(false);
            lastActionText=action+" · "+GetMatchModeName(true)+(HasAnyAI()?" · press Start Match":"");
            PublishAudio(AUDIO_RESET);Refresh();RequestSerialization();
        }

        public void SwapHumanSideAndReset()
        {
            if(GetAIControlCount()!=1){SetActionMessage("Swap sides is only available for Player vs AI");return;}
            if(matchStarted&&gameState==STATE_PLAYING&&!CanLocalForceReset())
            {
                SetActionMessage("Side swaps are locked during a live match");
                return;
            }
            if(!RequireLocalMatchControl("Swap sides",true))return;
            int oldHuman=blackIsAI?WHITE:BLACK;
            int oldId=oldHuman==BLACK?blackPlayerId:whitePlayerId;
            string oldName=oldHuman==BLACK?blackPlayerName:whitePlayerName;
            int newHuman=-oldHuman;
            if(!TakeLocalMatchOwnership()){SetActionMessage("Swap sides denied · authoritative Go owner unavailable");return;}
            if(aiController!=null)aiController.InvalidateSearch();
            blackIsAI=newHuman!=BLACK;whiteIsAI=newHuman!=WHITE;humanSide=newHuman;
            blackPlayerId=-1;blackPlayerName="";whitePlayerId=-1;whitePlayerName="";
            aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";
            if(oldId>=0){if(newHuman==BLACK){blackPlayerId=oldId;blackPlayerName=oldName;}else{whitePlayerId=oldId;whitePlayerName=oldName;}}
            ResetState(false);lastActionText="Sides swapped · "+(newHuman==BLACK?"Black":"White")+" player seat retained";PublishAudio(AUDIO_RESET);Refresh();RequestSerialization();
        }

        public void ClaimBlack(){ClaimSide(BLACK);}
        public void ClaimWhite(){ClaimSide(WHITE);}
        public void ClaimHuman()
        {
            if(GetAIControlCount()==1)ClaimSide(blackIsAI?WHITE:BLACK);
            else if(GetAIControlCount()==0)ClaimSide(starter);
            else SetActionMessage("Choose the Black or White seat");
        }

        public void ReleaseLocalSeat()
        {
            int id=LocalPlayerId();string name=LocalPlayerName();
            if(!SeatIdentityReferences(blackPlayerId,blackPlayerName,id,name)&&!SeatIdentityReferences(whitePlayerId,whitePlayerName,id,name)){SetActionMessage("You do not occupy a Go seat");return;}
            if(!TakeLocalMatchOwnership()){SetActionMessage("Seat release denied · authoritative Go owner unavailable");return;}
            if(ClearSeatForIdentity(id,name)){ClearPendingOffers();revision++;lastActionText="Go seat released";Refresh();RequestSerialization();}
        }

        private bool ClaimSide(int side)
        {
            if(IsAIControlled(side)){SetActionMessage((side==BLACK?"Black":"White")+" seat is controlled by AI");return false;}
            // PvP may start with one seated player, so a vacant opposing seat
            // remains claimable after Start Match. Before Start Match the seat
            // is a lobby claim: a later player may take over an occupied
            // unstarted PvAI/PvP seat, matching the Xiangqi table behaviour.
            // Once play has begun, a connected player is never displaced.
            int id=LocalPlayerId();string name=LocalPlayerName();int occupied=side==BLACK?blackPlayerId:whitePlayerId;string occupiedName=side==BLACK?blackPlayerName:whitePlayerName;int opposite=side==BLACK?whitePlayerId:blackPlayerId;string oppositeName=side==BLACK?whitePlayerName:blackPlayerName;
            if(SeatIdentityMatches(opposite,oppositeName,id,name)){SetActionMessage("You already occupy the other Go seat");return false;}
            bool preMatchLobby=!matchStarted&&gameState==STATE_PLAYING&&moveCount==0;
            if(occupied>=0&&SeatIdentityMatches(occupied,occupiedName,id,name))return true;
            if(occupied>=0&&!preMatchLobby){SetActionMessage((side==BLACK?"Black":"White")+" seat is occupied");return false;}
            if(!TakeLocalMatchOwnership()){SetActionMessage("Seat claim denied · authoritative Go owner unavailable");return false;}
            if(side==BLACK){blackPlayerId=id;blackPlayerName=name;}else{whitePlayerId=id;whitePlayerName=name;}ClearPendingOffers();revision++;turnStartedServerSecond=0;lastActionText=name+" joined "+(side==BLACK?"Black":"White")+" seat";Refresh();RequestSerialization();return true;
        }

        public int GetLocalSeatSide()
        {
            int id=LocalPlayerId();string name=LocalPlayerName();
            if(SeatIdentityMatches(blackPlayerId,blackPlayerName,id,name))return BLACK;
            if(SeatIdentityMatches(whitePlayerId,whitePlayerName,id,name))return WHITE;
            return EMPTY;
        }

        public bool CanLocalForceReset()
        {
            VRCPlayerApi local=Networking.LocalPlayer;
            if(!Utilities.IsValid(local))return true;
            int id=local.playerId;string name=local.displayName;
            // A non-playing table is a public recovery/lobby surface. This
            // keeps a finished or abandoned table usable by anyone.
            if(!matchStarted||gameState!=STATE_PLAYING)return true;
            // During a live turn only the instance/table owner, the human
            // player whose turn is current, or the synchronized 50-second
            // recovery deadline may reset. Spectators cannot erase an active
            // position before that deadline.
            if(Networking.IsMaster||IsLocalOwner())return true;
            if(IsForceResetTimeoutOpen())return true;
            if(GetLocalSeatSide()==sideToMove&&!IsAIControlled(sideToMove))return true;
            if(blackIsAI&&whiteIsAI&&IsLocalAIControlAuthority(id,name))return true;
            return false;
        }

        public int GetForceResetCooldownSeconds()
        {
            if(!matchStarted||gameState!=STATE_PLAYING)return 0;
            if(turnStartedServerSecond<=0)return FORCE_RESET_STALL_SECONDS;
            int elapsed=CurrentServerSecond()-turnStartedServerSecond;
            if(elapsed<0)elapsed=0;
            return Mathf.Max(0,FORCE_RESET_STALL_SECONDS-elapsed);
        }

        public bool IsForceResetTimeoutOpen()
        {
            return matchStarted&&gameState==STATE_PLAYING&&
                turnStartedServerSecond>0&&GetForceResetCooldownSeconds()<=0;
        }

        private int CurrentServerSecond()
        {
            if(!Utilities.IsValid(Networking.LocalPlayer))return 0;
            int value=(int)Networking.GetServerTimeInSeconds();
            return value<0?0:value;
        }

        private void StampTurnTimer()
        {
            if(matchStarted&&gameState==STATE_PLAYING)
            {
                turnStartedServerSecond=CurrentServerSecond();
                if(turnStartedServerSecond<=0)turnStartedServerSecond=1;
            }
            else turnStartedServerSecond=0;
        }

        /// <summary>
        /// Match-control permission for normal setup actions (mode, starter,
        /// settings, start and New Game). It includes either seated player,
        /// while Force Reset remains restricted to the table owner/admin.
        /// </summary>
        public bool CanLocalMatchControl()
        {
            VRCPlayerApi local=Networking.LocalPlayer;
            if(!Utilities.IsValid(local))return true;
            int id=local.playerId;string name=local.displayName;
            if(Networking.IsMaster||IsLocalOwner())return true;
            if(SeatIdentityMatches(blackPlayerId,blackPlayerName,id,name)||
                SeatIdentityMatches(whitePlayerId,whitePlayerName,id,name))return true;
            if(blackIsAI&&whiteIsAI&&IsLocalAIControlAuthority(id,name))return true;
            // An entirely vacant, unstarted table is a shared lobby. Once a
            // human seat is occupied, only that player/table owner may change
            // mode, starter or settings; other visitors must claim the open
            // seat first. ClaimSide itself remains deliberately clickable so
            // a late visitor can replace an unstarted lobby seat.
            bool vacant=(!blackIsAI&&blackPlayerId<0)||
                (!whiteIsAI&&whitePlayerId<0);
            if(!matchStarted&&gameState==STATE_PLAYING&&moveCount==0&&vacant)
                return true;
            return false;
        }

        /// <summary>
        /// Undo is a turn-scoped match action. In PvP only the seated player
        /// whose turn is current may request an undo; the opposite seated
        /// player may only accept that current, revision-bound request. This
        /// prevents spectators and an out-of-turn player from repeatedly
        /// rewriting the table while preserving explicit two-player consent.
        /// In PvAI/AIvP the seated human may roll back their decision point.
        /// AIvAI remains limited to its controller/owner.
        /// </summary>
        public bool CanLocalUndo()
        {
            if(moveCount<=0||gameState!=STATE_PLAYING||!matchStarted)return false;
            VRCPlayerApi local=Networking.LocalPlayer;
            if(!Utilities.IsValid(local))return true;
            int localSide=GetLocalSeatSide();
            if(GetAIControlCount()==0)
            {
                if(localSide==EMPTY)return false;
                if(localSide==sideToMove)return true;
                return undoOfferSide==-localSide&&
                    IsOfferCurrent(undoOfferSide,undoOfferRevision,undoOfferMoveCount);
            }
            if(GetAIControlCount()==1)return localSide!=EMPTY;
            int id=local.playerId;string name=local.displayName;
            return Networking.IsMaster||IsLocalOwner()||IsLocalAIControlAuthority(id,name);
        }

        private bool RequireLocalMatchControl(string action,bool allowWhenVacant)
        {
            VRCPlayerApi local=Networking.LocalPlayer;if(!Utilities.IsValid(local))return true;
            int id=local.playerId;string name=local.displayName;
            if(SeatIdentityMatches(blackPlayerId,blackPlayerName,id,name)||SeatIdentityMatches(whitePlayerId,whitePlayerName,id,name))return true;
            if(blackIsAI&&whiteIsAI&&(aiControlAuthorityName==null||aiControlAuthorityName.Length==0||IsLocalAIControlAuthority(id,name)||Networking.IsMaster||IsLocalOwner()))return true;
            bool vacant=(!blackIsAI&&blackPlayerId<0)||(!whiteIsAI&&whitePlayerId<0);
            if(allowWhenVacant&&!matchStarted&&gameState==STATE_PLAYING&&
                moveCount==0&&vacant)return true;
            SetActionMessage(action+" requires a seated Go player or table controller");return false;
        }

        private bool EnsureLocalSeat(int side)
        {
            if(IsAIControlled(side)){SetActionMessage("This Go turn belongs to AI");return false;}
            if(!Utilities.IsValid(Networking.LocalPlayer))return true;
            int id=LocalPlayerId();string name=LocalPlayerName();int occupied=side==BLACK?blackPlayerId:whitePlayerId;string occupiedName=side==BLACK?blackPlayerName:whitePlayerName;
            if(occupied>=0&&!SeatIdentityMatches(occupied,occupiedName,id,name)){SetActionMessage("This Go seat belongs to another player");return false;}
            return occupied>=0||ClaimSide(side);
        }

        private bool SanitizeSynchronizedState()
        {
            bool changed=false;
            if(EnsureHistoryStoneCountStorage())changed=true;
            if(EnsureMoveHistoryStorage())changed=true;
            if(starter!=BLACK&&starter!=WHITE){starter=BLACK;changed=true;}
            if(humanSide!=BLACK&&humanSide!=WHITE){humanSide=BLACK;changed=true;}
            if(blackPlayerName==null){blackPlayerName="";changed=true;}
            if(whitePlayerName==null){whitePlayerName="";changed=true;}
            if(aiControlAuthorityName==null){aiControlAuthorityName="";changed=true;}
            if(aiControlAuthorityPlayerId<0&&aiControlAuthorityName.Length>0)
            {aiControlAuthorityName="";changed=true;}
            if(blackIsAI&&blackPlayerId>=0){blackPlayerId=-1;blackPlayerName="";changed=true;}
            if(whiteIsAI&&whitePlayerId>=0){whitePlayerId=-1;whitePlayerName="";changed=true;}
            if(blackPlayerId>=0&&blackPlayerId==whitePlayerId){whitePlayerId=-1;whitePlayerName="";changed=true;}
            if(!blackIsAI&&!whiteIsAI&&
                (aiControlAuthorityPlayerId>=0||aiControlAuthorityName.Length>0))
            {aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";changed=true;}
            if(!(blackIsAI&&whiteIsAI)&&(aiControlAuthorityPlayerId>=0||aiControlAuthorityName.Length>0)){aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";changed=true;}
            if(blackIsAI&&whiteIsAI&&aiControlAuthorityPlayerId>=0&&
                !IsSeatConnected(aiControlAuthorityPlayerId,aiControlAuthorityName))
            {aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";changed=true;}
            if(blackIsAI!=whiteIsAI)
            {
                int expected=blackIsAI?WHITE:BLACK;
                if(humanSide!=expected){humanSide=expected;changed=true;}
            }
            if(positionHistoryCount<0){positionHistoryCount=0;changed=true;}
            if(positionHistoryCount>MAX_POSITION_HISTORY){positionHistoryCount=MAX_POSITION_HISTORY;changed=true;}
            if(moveCount<0){moveCount=0;changed=true;}
            if(moveCount>MAX_MOVES){moveCount=MAX_MOVES;changed=true;}
            int expectedPositionHistoryCount=moveCount+1;
            if(expectedPositionHistoryCount<=MAX_POSITION_HISTORY&&
                positionHistoryCount!=expectedPositionHistoryCount)
            {
                // The position index is derived from the initial position plus
                // one entry per accepted move/pass. Repairing only this scalar
                // lets a legacy/partial snapshot re-enter the verified replay
                // path without inventing any rule history.
                positionHistoryCount=expectedPositionHistoryCount;changed=true;
            }
            if(consecutivePasses<0){consecutivePasses=0;changed=true;}
            if(consecutivePasses>2){consecutivePasses=2;changed=true;}
            if(blackCaptures<0){blackCaptures=0;changed=true;}
            if(whiteCaptures<0){whiteCaptures=0;changed=true;}
            if(gameState<STATE_PLAYING||gameState>STATE_RESIGNED){gameState=STATE_PLAYING;changed=true;}
            if(sideToMove!=BLACK&&sideToMove!=WHITE){sideToMove=starter;changed=true;}
            if(turnStartedServerSecond<0){turnStartedServerSecond=0;changed=true;}
            if(!matchStarted||gameState!=STATE_PLAYING)
            {
                if(turnStartedServerSecond!=0){turnStartedServerSecond=0;changed=true;}
            }
            else if(turnStartedServerSecond==0&&IsLocalOwner())
            {
                StampTurnTimer();changed=true;
            }
            if(hintMove!=NONE && hintMove!=PASS &&
                (hintMove<0 || hintMove>=AREA || board[hintMove]!=EMPTY || gameState!=STATE_PLAYING))
            { hintMove=NONE; hintRevision++; changed=true; }
            if(gameState!=STATE_PLAYING && hintMove!=NONE)
            { hintMove=NONE; hintRevision++; changed=true; }
            if(aiHintPermissionRevision<0){aiHintPermissionRevision=0;changed=true;}
            if(matchStarted&&!aiHintPermissionLocked)
            { aiHintPermissionLocked=true;changed=true; }
            if(!matchStarted&&aiHintPermissionLocked)
            { aiHintPermissionLocked=false;changed=true; }
            // PvP now uses the same explicit Start Match gate as AI modes so
            // both seat buttons remain usable before play begins.  Do not
            // silently auto-start a synchronized PvP snapshot here.
            if(HasAnyAI()&&moveCount>0&&!matchStarted){matchStarted=true;changed=true;}
            if(gameState==STATE_PLAYING&&IsAtCapacity()&&IsLocalOwner())
            {
                FinishByMoveLimit();
                hintMove=NONE;
                hintRevision++;
                changed=true;
            }
            if(blackPlayerId>=0&&!IsSeatConnected(blackPlayerId,blackPlayerName)){blackPlayerId=-1;blackPlayerName="";changed=true;}
            if(whitePlayerId>=0&&!IsSeatConnected(whitePlayerId,whitePlayerName)){whitePlayerId=-1;whitePlayerName="";changed=true;}
            bool undoFieldsPresent=undoOfferSide!=EMPTY||undoOfferRevision!=0||undoOfferMoveCount!=-1;
            if(undoFieldsPresent&&(!IsOfferCurrent(undoOfferSide,undoOfferRevision,undoOfferMoveCount)||GetAIControlCount()!=0||gameState!=STATE_PLAYING||!matchStarted||!IsOfferSideSeated(undoOfferSide)))
            {ClearPendingUndo();changed=true;}
            bool drawFieldsPresent=drawOfferSide!=EMPTY||drawOfferRevision!=0||drawOfferMoveCount!=-1;
            if(drawFieldsPresent&&(!IsOfferCurrent(drawOfferSide,drawOfferRevision,drawOfferMoveCount)||GetAIControlCount()!=0||gameState!=STATE_PLAYING||!matchStarted||!IsOfferSideSeated(drawOfferSide)))
            {ClearPendingDraw();changed=true;}
            return changed;
        }

        private bool ClearSeatForIdentity(int playerId,string playerName)
        {
            bool changed=false;
            if(SeatIdentityReferences(blackPlayerId,blackPlayerName,playerId,playerName)){blackPlayerId=-1;blackPlayerName="";changed=true;}
            if(SeatIdentityReferences(whitePlayerId,whitePlayerName,playerId,playerName)){whitePlayerId=-1;whitePlayerName="";changed=true;}
            return changed;
        }

        private bool ClearAIControlAuthorityForIdentity(int playerId,string playerName)
        {
            if(aiControlAuthorityPlayerId<0)return false;
            if(aiControlAuthorityPlayerId==playerId)
            {
                aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";return true;
            }
            bool sameName=aiControlAuthorityName!=null&&aiControlAuthorityName.Length>0&&
                playerName!=null&&playerName.Length>0&&aiControlAuthorityName==playerName;
            if(sameName&&!IsSeatConnected(aiControlAuthorityPlayerId,aiControlAuthorityName))
            {
                aiControlAuthorityPlayerId=-1;aiControlAuthorityName="";return true;
            }
            return false;
        }

        private bool IsSeatConnected(int playerId,string expectedName)
        {
            if(playerId<0)return false;
            if(!Utilities.IsValid(Networking.LocalPlayer))return true;
            VRCPlayerApi player=VRCPlayerApi.GetPlayerById(playerId);
            if(!Utilities.IsValid(player))return false;
            return expectedName==null||expectedName.Length==0||player.displayName==expectedName;
        }

        private bool IsSeatVacantOrStale(int playerId,string playerName){return playerId<0||!IsSeatConnected(playerId,playerName);}

        private bool IsOfferSideSeated(int side)
        {
            if(side==BLACK)return !blackIsAI&&blackPlayerId>=0;
            if(side==WHITE)return !whiteIsAI&&whitePlayerId>=0;
            return false;
        }

        private bool SeatIdentityMatches(int seatPlayerId,string seatPlayerName,int playerId,string playerName)
        {
            if(seatPlayerId!=playerId)return false;
            return seatPlayerName==null||seatPlayerName.Length==0||seatPlayerName==playerName;
        }

        private bool SeatIdentityReferences(int seatPlayerId,string seatPlayerName,int playerId,string playerName)
        {
            if(seatPlayerId<0)return false;
            if(seatPlayerId==playerId)
            {
                bool compatible=seatPlayerName==null||seatPlayerName.Length==0||playerName==null||playerName.Length==0||seatPlayerName==playerName;
                if(compatible)return true;
                if(!Utilities.IsValid(Networking.LocalPlayer))return true;
                return !IsSeatConnected(seatPlayerId,seatPlayerName);
            }
            bool sameName=seatPlayerName!=null&&seatPlayerName.Length>0&&playerName!=null&&playerName.Length>0&&seatPlayerName==playerName;
            if(!sameName)return false;
            if(!Utilities.IsValid(Networking.LocalPlayer))return true;
            return !IsSeatConnected(seatPlayerId,seatPlayerName);
        }

        private bool IsLocalAIControlAuthority(int playerId,string playerName)
        {
            if(aiControlAuthorityName==null||aiControlAuthorityName.Length==0)return false;
            if(aiControlAuthorityPlayerId==playerId&&aiControlAuthorityName==playerName)return true;
            if(aiControlAuthorityName!=playerName)return false;
            if(!Utilities.IsValid(Networking.LocalPlayer))return true;
            return !IsSeatConnected(aiControlAuthorityPlayerId,aiControlAuthorityName);
        }

        public string GetLocalizedBoardTitle(bool english)
        {
            return english?"Udon Go AI · 19×19":"Udon Go AI · 19×19";
        }

        public string GetCoordinateLabel(int loc)
        {
            if(loc==PASS)return "PASS";
            if(loc<0||loc>=AREA)return "—";
            int x=loc%SIZE,y=loc/SIZE;string cols="ABCDEFGHJKLMNOPQRST";
            return cols.Substring(x,1)+(SIZE-y);
        }

        public string GetLastActionLocalized(bool english)
        {
            if(english||lastActionText==null)return lastActionText;
            string value=lastActionText;
            value=value.Replace("New game · press Start Match","新局 · 请点击开始");
            value=value.Replace("New game","新局");
            value=value.Replace("Force reset · press Start Match","强制重置 · 请点击开始");
            value=value.Replace("Force reset","强制重置");
            value=value.Replace("Force reset denied · another player's Go seat is active","强制重置已拒绝 · 另一位围棋玩家正在行棋");
            value=value.Replace("Black ","黑方 ").Replace("White ","白方 ");
            value=value.Replace(" · capture "," · 提子 ");
            value=value.Replace(" requested undo · waiting for opponent"," 请求悔棋 · 等待对手");
            value=value.Replace(" offered a draw · waiting for opponent"," 提出和棋 · 等待对手");
            value=value.Replace("Your undo request is waiting for the other player","你的悔棋请求正在等待对手");
            value=value.Replace("Undo requires the current Go player; the other player must accept","悔棋需要当前行棋玩家发起；另一方必须同意");
            value=value.Replace("Undo requires a seated Go player; spectators cannot change this table","悔棋需要已入座玩家；旁观者不能修改本桌");
            value=value.Replace(" requires a seated Go player or table controller"," 需要已入座玩家或本桌控制者");
            value=value.Replace("Your draw offer is waiting for the other player","你的和棋请求正在等待对手");
            value=value.Replace("Both players accepted a draw","双方同意和棋");
            value=value.Replace("Undid ","已撤销 ");
            value=value.Replace("AI hint requested · searching without auto-play","AI 提示计算中 · 不会自动落子");
            value=value.Replace("AI hint · pass","AI 提示 · 建议停一手");
            value=value.Replace("AI hint · ","AI 提示 · ");
            value=value.Replace("AI hint found no legal move","AI 提示未找到合法着法");
            value=value.Replace("Match started · ","对局开始 · ");
            value=value.Replace("Pass","停一手").Replace("Black resigned","黑方认输").Replace("White resigned","白方认输");
            return value;
        }

        private void SetActionMessage(string value){lastActionText=value;Refresh();}

        private bool IsLocalOwner()
        {
            VRCPlayerApi local=Networking.LocalPlayer;
            return !Utilities.IsValid(local)||Networking.IsOwner(gameObject);
        }

        private void MarkDerivedHistoryReady()
        {
            derivedHistoryReady=true;derivedHistoryRebuildFailed=false;
            derivedHistoryRevision=revision;derivedHistoryMoveCount=moveCount;
            derivedHistoryPositionHistoryCount=positionHistoryCount;
            derivedHistoryHash0=h0;derivedHistoryHash1=h1;
            derivedHistoryHash2=h2;derivedHistoryHash3=h3;
            localHistoryRebuildFailed=false;
            InvalidateMoveMaskCache();
        }

        private bool EnsureDerivedHistory()
        {
            bool samePosition=derivedHistoryMoveCount==moveCount&&
                derivedHistoryPositionHistoryCount==positionHistoryCount&&
                derivedHistoryHash0==h0&&derivedHistoryHash1==h1&&
                derivedHistoryHash2==h2&&derivedHistoryHash3==h3;
            if(derivedHistoryReady&&samePosition)return true;
            if(derivedHistoryRebuildFailed&&samePosition)return false;
            return RebuildDerivedHistoryFromMoveLog();
        }

        /// <summary>
        /// Reconstructs local positional hashes from the synchronized board and
        /// accepted move log. The replay is checked against the received board
        /// and derived scalar state before it is accepted. This keeps the
        /// compact snapshot exact without trusting a remote cache.
        /// </summary>
        private bool RebuildDerivedHistoryFromMoveLog()
        {
            EnsureHistoryStoneCountStorage();
            EnsureMoveHistoryStorage();
            if(!CaptureRebuildTransaction())
            {
                derivedHistoryReady=false;derivedHistoryRebuildFailed=true;
                localHistoryRebuildFailed=true;return false;
            }
            int savedMoveCount=moveCount;
            int savedPositionHistoryCount=positionHistoryCount;
            int savedSideToMove=sideToMove;
            int savedConsecutivePasses=consecutivePasses;
            int savedBlackCaptures=blackCaptures;
            int savedWhiteCaptures=whiteCaptures;
            int savedKoLoc=koLoc;
            int savedLastMove=lastMove;
            int savedGameState=gameState;
            int savedWinner=winner;
            int savedFinalBlackArea=finalBlackArea;
            int savedFinalWhiteArea=finalWhiteArea;
            float savedFinalScore=finalWhiteMinusBlackScore;
            string savedAction=lastActionText;
            bool valid=savedMoveCount>=0&&savedMoveCount<=MAX_MOVES&&
                savedPositionHistoryCount>=0&&savedPositionHistoryCount<=MAX_POSITION_HISTORY;
            if(valid)valid=RebuildPositionToMoveCount(savedMoveCount);
            if(valid)
            {
                for(int i=0;i<AREA;i++)
                    if(board[i]!=rebuildBoardSnapshot[i]){valid=false;break;}
            }
            if(valid)
            {
                for(int i=0;i<AREA;i++)
                {
                    if(previousBoard1[i]!=rebuildPreviousBoard1Snapshot[i]||
                        previousBoard2[i]!=rebuildPreviousBoard2Snapshot[i])
                    {
                        valid=false;break;
                    }
                }
            }
            if(valid)
            {
                for(int i=0;i<5;i++)
                {
                    if(recentMoveLoc[i]!=rebuildRecentMoveLocSnapshot[i]||
                        recentMovePla[i]!=rebuildRecentMovePlaSnapshot[i])
                    {
                        valid=false;break;
                    }
                }
            }
            if(valid&&moveCount!=savedMoveCount)valid=false;
            if(valid&&positionHistoryCount!=savedPositionHistoryCount)valid=false;
            if(valid&&sideToMove!=savedSideToMove)valid=false;
            if(valid&&consecutivePasses!=savedConsecutivePasses)valid=false;
            if(valid&&blackCaptures!=savedBlackCaptures)valid=false;
            if(valid&&whiteCaptures!=savedWhiteCaptures)valid=false;
            if(valid&&koLoc!=savedKoLoc)valid=false;
            if(valid&&lastMove!=savedLastMove)valid=false;
            if(!valid)
            {
                RestoreRebuildTransaction();
                InvalidateMoveMaskCache();
                derivedHistoryReady=false;derivedHistoryRebuildFailed=true;
                derivedHistoryRevision=revision;derivedHistoryMoveCount=savedMoveCount;
                derivedHistoryPositionHistoryCount=savedPositionHistoryCount;
                derivedHistoryHash0=h0;derivedHistoryHash1=h1;
                derivedHistoryHash2=h2;derivedHistoryHash3=h3;
                localHistoryRebuildFailed=true;
                return false;
            }

            // Replay intentionally sets a playing state; restore the received
            // terminal/result presentation after the rule history is built.
            gameState=savedGameState;winner=savedWinner;
            finalBlackArea=savedFinalBlackArea;finalWhiteArea=savedFinalWhiteArea;
            finalWhiteMinusBlackScore=savedFinalScore;lastActionText=savedAction;
            historyStoneCountVersion=1;InvalidateMoveMaskCache();
            derivedHistoryReady=true;derivedHistoryRebuildFailed=false;
            derivedHistoryRevision=revision;derivedHistoryMoveCount=moveCount;
            derivedHistoryPositionHistoryCount=positionHistoryCount;
            derivedHistoryHash0=h0;derivedHistoryHash1=h1;derivedHistoryHash2=h2;derivedHistoryHash3=h3;
            localHistoryRebuildFailed=false;
            return true;
        }

        public bool RebuildDerivedHistory()
        {
            derivedHistoryReady=false;
            derivedHistoryRebuildFailed=false;
            return RebuildDerivedHistoryFromMoveLog();
        }

        /// <summary>
        /// Warms the authoritative position's legal/superko mask in bounded
        /// chunks. Board hover can therefore read an O(1) cached result after
        /// the position settles instead of doing a complete tentative move
        /// simulation for every pointer crossing. The requested budget is
        /// clamped so one caller can never turn this into a 361-point Tick.
        /// </summary>
        public void StepMoveMaskCache(int pointBudget)
        {
            // A non-owner must not warm a locally manufactured pre-snapshot
            // position while waiting for the authoritative network payload.
            // The canonical empty position has one history entry, so a zero
            // count is an uninitialized snapshot rather than a playable board.
            if(!IsLocalOwner()&&positionHistoryCount<=0)return;
            if(!EnsureDerivedHistory())return;
            EnsureMoveMaskCache();
            if(!moveMaskCacheBuilding)return;
            int budget=Mathf.Clamp(pointBudget,1,48);
            float startedAt=Time.realtimeSinceStartup;
            int scanned=0;
            int computed=0;
            while(moveMaskCacheCursor<AREA&&scanned<budget)
            {
                int loc=moveMaskCacheCursor++;
                scanned++;
                if(moveMaskComputedGeneration[loc]==moveMaskCacheGeneration)continue;
                GetMoveMask(loc);
                computed++;
            }
            if(computed>0)
            {
                moveMaskCachePoints+=computed;
                moveMaskCacheFrames++;
                if(computed>maxMoveMaskPointsOneFrame)maxMoveMaskPointsOneFrame=computed;
            }
            float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
            if(elapsed>maxMoveMaskFrameMilliseconds)maxMoveMaskFrameMilliseconds=elapsed;
            if(moveMaskCacheCursor>=AREA)
            {
                moveMaskCacheBuilding=false;
                moveMaskCacheActive=false;
            }
        }

        /// <summary>
        /// Advances the exact authoritative move-mask cache until the caller's
        /// small CPU slice expires. Each point is still evaluated by the same
        /// GetMoveMask implementation; this method only changes how many
        /// already-independent points are admitted in one rendered frame.
        /// </summary>
        public int StepMoveMaskCacheTimeSliced(float milliseconds,int pointCap)
        {
            // Keep the same snapshot/derived-history guards as the legacy
            // chunked entry point. A remote client must never manufacture a
            // local cache while it is waiting for its first authoritative
            // snapshot.
            if(!IsLocalOwner()&&positionHistoryCount<=0)return 0;
            if(!EnsureDerivedHistory())return 0;
            EnsureMoveMaskCache();
            if(!moveMaskCacheBuilding)return 0;

            float budgetMs=Mathf.Clamp(milliseconds,
                MOVE_MASK_WARMUP_MIN_MS,MOVE_MASK_WARMUP_MAX_MS);
            int cap=Mathf.Clamp(pointCap,1,48);
            float startedAt=Time.realtimeSinceStartup;
            float deadline=startedAt+budgetMs*0.001f;
            int computed=0;
            int scanned=0;
            // A point cap is a resolution-independent safety guard for
            // platforms whose realtime clock has coarse granularity. The
            // deadline remains the primary budget authority.
            while(moveMaskCacheCursor<AREA&&scanned<cap)
            {
                if(scanned>0&&Time.realtimeSinceStartup>=deadline)break;
                int beforeCursor=moveMaskCacheCursor;
                int beforePoints=moveMaskCachePoints;
                // One exact point per internal call. Do not pass the whole
                // quota here or a helper could recreate a hidden burst.
                StepMoveMaskCache(1);
                scanned++;
                int delta=moveMaskCachePoints-beforePoints;
                if(delta>0)computed+=delta;
                // Protect against a platform clock that does not advance and
                // against any future no-progress cache state.
                if(moveMaskCacheCursor<=beforeCursor)break;
                if(IsMoveMaskCacheCompleteForCurrentRevision())break;
            }
            float elapsed=(Time.realtimeSinceStartup-startedAt)*1000f;
            if(computed>0)
            {
                // StepMoveMaskCache recorded each single-point call as a
                // frame. Fold those internal samples into one rendered-frame
                // sample so existing telemetry keeps its original meaning.
                moveMaskCacheFrames-=computed-1;
                if(moveMaskCacheFrames<0)moveMaskCacheFrames=0;
                if(computed>maxMoveMaskPointsOneFrame)
                    maxMoveMaskPointsOneFrame=computed;
            }
            if(elapsed>maxMoveMaskFrameMilliseconds)
                maxMoveMaskFrameMilliseconds=elapsed;
            return computed;
        }

        public bool IsMoveMaskCacheComplete()
        {
            return moveMaskCacheValid&&!moveMaskCacheBuilding;
        }

        public bool IsMoveMaskCacheCompleteForCurrentRevision()
        {
            return IsMoveMaskCacheComplete()&&moveMaskCacheRevision==revision;
        }

        public bool IsMoveMaskWarmupDeferred()
        {
            return moveMaskWarmupDeferredUntilFrame>Time.frameCount;
        }

        public void ResetMoveMaskCacheForVerifier()
        {
            InvalidateMoveMaskCache();
        }

        public void ResetMoveMaskTelemetry()
        {
            moveMaskCachePoints=0;moveMaskCacheFrames=0;
            moveMaskCacheHits=0;moveMaskCacheMisses=0;
            maxMoveMaskPointsOneFrame=0;maxMoveMaskFrameMilliseconds=0f;
        }

        private void InvalidateMoveMaskCache()
        {
            moveMaskCacheValid=false;
            moveMaskCacheBuilding=false;
            moveMaskCacheActive=false;
            moveMaskCacheCursor=0;
            if(boardPool!=null)boardPool.WakeMoveMaskTable(tableIndex);
        }

        private void EnsureMoveMaskCache()
        {
            if(moveMaskCacheValid&&moveMaskCacheRevision==revision)return;
            moveMaskCacheGeneration++;
            // A signed generation cannot realistically wrap during a match,
            // but reset the stamps if it ever does so no stale point can be
            // mistaken for the new cache.
            if(moveMaskCacheGeneration<=0)
            {
                for(int i=0;i<AREA;i++)moveMaskComputedGeneration[i]=0;
                moveMaskCacheGeneration=1;
            }
            moveMaskCacheRevision=revision;
            moveMaskCacheCursor=0;
            moveMaskCacheValid=true;
            moveMaskCacheBuilding=true;
            moveMaskCacheActive=true;
        }

        private bool HistoryMayContainStoneCount(int stoneCount)
        {
            if(historyStoneCountVersion<=0)return true;
            EnsureHistoryStoneCountFrequency();
            // A malformed/legacy derived cache must never make a legal move
            // appear safe. Fall back to the exact hash scan until the local
            // index has been rebuilt from a complete prefix.
            if(!historyStoneCountFrequencyValid)return true;
            return stoneCount>=0&&stoneCount<=AREA&&
                historyStoneCountFrequency[stoneCount]>0;
        }

        private bool TakeLocalMatchOwnership()
        {
            if(!TakeOwnership())return false;
            if(blackIsAI&&whiteIsAI){aiControlAuthorityPlayerId=LocalPlayerId();aiControlAuthorityName=LocalPlayerName();}
            return true;
        }

        public int GetChineseWhiteHandicapBonusTimes2()
        {
            // KataGo Chinese rules use whiteHandicapBonus=N.  The raw
            // komiTimes2 field remains the synchronized ordinary komi; the
            // handicap bonus is added only for area scoring.
            if(!areaScoring||handicapStones<=0)return 0;
            return Mathf.Max(0,handicapStones)*2;
        }

        public int GetEffectiveChineseWhiteCompensationTimes2()
        {
            // Effective white compensation is ordinary komi plus the Chinese
            // handicap bonus.  No caller may add this bonus a second time.
            return komiTimes2+GetChineseWhiteHandicapBonusTimes2();
        }

        public float GetEffectiveChineseWhiteCompensationPoints()
        {
            return GetEffectiveChineseWhiteCompensationTimes2()*0.5f;
        }

        public bool HasTerminalBoardScore()
        {
            if(gameState==STATE_PLAYING||gameState==STATE_RESIGNED)return false;
            return consecutivePasses>=2||moveCount>=MAX_MOVES||
                positionHistoryCount>=MAX_POSITION_HISTORY;
        }

        private float ComputeWhiteMinusBlackAreaScoreInternal(bool commitFinal)
        {
            int b=0,w=0;
            for(int i=0;i<AREA;i++)
            {
                if(board[i]==BLACK)b++;
                else if(board[i]==WHITE)w++;
                regionVisited[i]=false;
            }
            for(int start=0;start<AREA;start++)
            {
                if(board[start]!=EMPTY||regionVisited[start])continue;
                int head=0,tail=0,count=0;bool tb=false,tw=false;
                queue[tail++]=start;regionVisited[start]=true;
                while(head<tail)
                {
                    int loc=queue[head++];count++;
                    TouchRegion(Left(loc),ref tail,ref tb,ref tw);
                    TouchRegion(Right(loc),ref tail,ref tb,ref tw);
                    TouchRegion(Up(loc),ref tail,ref tb,ref tw);
                    TouchRegion(Down(loc),ref tail,ref tb,ref tw);
                }
                if(tb&&!tw)b+=count;
                else if(tw&&!tb)w+=count;
            }
            float score=w+GetEffectiveChineseWhiteCompensationPoints()-b;
            if(commitFinal)
            {
                finalBlackArea=b;
                finalWhiteArea=w;
                finalWhiteMinusBlackScore=score;
            }
            return score;
        }

        public float ComputeCurrentWhiteMinusBlackAreaScore()
        {
            // Read-only current-board preview.  The flood-fill uses the
            // private regionVisited/queue scratch arrays, but it never writes
            // the board, terminal fields, revision or any synchronized state.
            return ComputeWhiteMinusBlackAreaScoreInternal(false);
        }

        private float ComputeFinalWhiteMinusBlackAreaScore()
        {
            return ComputeWhiteMinusBlackAreaScoreInternal(true);
        }
        public float ComputeCurrentWhiteMinusBlackTerritoryScore()
        {
            int b=blackCaptures,w=whiteCaptures; for(int i=0;i<AREA;i++)regionVisited[i]=false;
            for(int start=0;start<AREA;start++){
                if(board[start]!=EMPTY||regionVisited[start])continue; int head=0,tail=0,count=0; bool tb=false,tw=false; queue[tail++]=start; regionVisited[start]=true;
                while(head<tail){ int loc=queue[head++]; count++; TouchRegion(Left(loc),ref tail,ref tb,ref tw); TouchRegion(Right(loc),ref tail,ref tb,ref tw); TouchRegion(Up(loc),ref tail,ref tb,ref tw); TouchRegion(Down(loc),ref tail,ref tb,ref tw); }
                if(tb&&!tw)b+=count; else if(tw&&!tb)w+=count;
            }
            float score=w+komiTimes2*0.5f-b;
            if(IsLocalOwner())
            {
                finalBlackArea=b;finalWhiteArea=w;finalWhiteMinusBlackScore=score;
            }
            return score;
        }
        private void FinishByScore(){ SetTerminalFromScore(areaScoring?"Game ended by two passes · area score":"Game ended by two passes · territory score"); }
        private void FinishByMoveLimit(){ SetTerminalFromScore(areaScoring?"Game ended by move limit · area score":"Game ended by move limit · territory score"); }
        private void SetTerminalFromScore(string action){ float s=areaScoring?ComputeFinalWhiteMinusBlackAreaScore():ComputeCurrentWhiteMinusBlackTerritoryScore(); if(s>0){winner=WHITE;gameState=STATE_WHITE_WINS;} else if(s<0){winner=BLACK;gameState=STATE_BLACK_WINS;} else{winner=EMPTY;gameState=STATE_DRAW;} turnStartedServerSecond=0; lastActionText=action; }

        private int CaptureDead(int start,int color,ref int single){ if(board[start]!=color)return 0; int size=CollectGroup(start,color); if(CountLiberties()!=0)return 0; single=size==1?group[0]:NONE; for(int i=0;i<size;i++)board[group[i]]=EMPTY; return size; }
        private int CollectGroup(int start,int color)
        {
            groupGeneration++;
            // A signed generation can cross zero after a very long-running
            // instance. Clear the stamps once at that unreachable boundary
            // so no stale mark can be mistaken for the new traversal.
            if(groupGeneration<=0)
            {
                for(int i=0;i<AREA;i++)
                {
                    groupVisitedStamp[i]=0;
                    libertyVisitedStamp[i]=0;
                }
                groupGeneration=1;
            }
            collectedGroupLibertyCount=0;
            int head=0,tail=0,size=0;
            queue[tail++]=start;
            groupVisitedStamp[start]=groupGeneration;
            while(head<tail)
            {
                int loc=queue[head++];
                group[size++]=loc;
                PushNeighbor(Left(loc),color,ref tail);
                PushNeighbor(Right(loc),color,ref tail);
                PushNeighbor(Up(loc),color,ref tail);
                PushNeighbor(Down(loc),color,ref tail);
            }
            return size;
        }
        private void PushNeighbor(int loc,int color,ref int tail)
        {
            if(loc<0)return;
            int c=board[loc];
            if(c==EMPTY)
            {
                if(libertyVisitedStamp[loc]!=groupGeneration)
                {
                    libertyVisitedStamp[loc]=groupGeneration;
                    collectedGroupLibertyCount++;
                }
                return;
            }
            if(c==color&&groupVisitedStamp[loc]!=groupGeneration)
            {
                groupVisitedStamp[loc]=groupGeneration;
                queue[tail++]=loc;
            }
        }
        private int CountLiberties(){return collectedGroupLibertyCount;}
        private void TouchRegion(int loc,ref int tail,ref bool tb,ref bool tw){if(loc<0)return;int c=board[loc];if(c==BLACK)tb=true;else if(c==WHITE)tw=true;else if(!regionVisited[loc]){regionVisited[loc]=true;queue[tail++]=loc;}}
        private void Reject(int oldB,int oldW,int oldKo){Copy(backup,board);blackCaptures=oldB;whiteCaptures=oldW;koLoc=oldKo;ComputeHash();}
        private void PushRecentMove(int pla,int loc){for(int i=4;i>0;i--){recentMoveLoc[i]=recentMoveLoc[i-1];recentMovePla[i]=recentMovePla[i-1];}recentMoveLoc[0]=loc;recentMovePla[0]=pla;}
        private void RecordMove(int pla,int loc)
        {
            if(moveHistory==null||moveCount<0||moveCount>=MAX_MOVES)return;
            moveHistory[moveCount]=(loc+2)|(pla==WHITE?MOVE_HISTORY_WHITE_MASK:0);
        }
        private int DecodeMoveLocation(int packed){return (packed&MOVE_HISTORY_LOCATION_MASK)-2;}
        private int DecodeMovePlayer(int packed){return (packed&MOVE_HISTORY_WHITE_MASK)==0?BLACK:WHITE;}
        private bool IsValidPackedMove(int packed)
        {
            return packed>0&&
            (packed&~(MOVE_HISTORY_LOCATION_MASK|MOVE_HISTORY_WHITE_MASK))==0;
        }
        private bool IsOfferCurrent(int side,int offerRevision,int offerMoveCount)
        {
            return (side==BLACK||side==WHITE)&&offerRevision==revision&&offerMoveCount==moveCount&&offerMoveCount>0;
        }
        private void ClearPendingUndo(){undoOfferSide=EMPTY;undoOfferRevision=0;undoOfferMoveCount=-1;}
        private void ClearPendingDraw(){drawOfferSide=EMPTY;drawOfferRevision=0;drawOfferMoveCount=-1;}
        private void ClearPendingOffers(){ClearPendingUndo();ClearPendingDraw();}

        private void CommitUndo(int targetMoveCount,int undoCount)
        {
            int previousMoveCount=moveCount;
            if(targetMoveCount<0||!TryRebuildPositionToMoveCount(targetMoveCount))
            {
                SetActionMessage("Undo denied · synchronized move history is incomplete");
                return;
            }
            if(aiController!=null)aiController.InvalidateSearch();
            for(int i=targetMoveCount;i<previousMoveCount&&i<MAX_MOVES;i++)moveHistory[i]=0;
            ClearPendingOffers();hintMove=NONE;hintRevision++;revision++;
            StampTurnTimer();
            MarkDerivedHistoryReady();
            lastActionText="Undid "+undoCount+" Go move"+(undoCount==1?"":"s");
            pendingBoardRefreshReason=GoBoardView.REFRESH_UNDO;
            PublishAudio(AUDIO_RESET);Refresh();RequestSerialization();
        }

        private bool RebuildPositionToMoveCount(int targetMoveCount)
        {
            if(targetMoveCount<0||targetMoveCount>moveCount||targetMoveCount>MAX_MOVES)return false;
            EnsureMoveHistoryStorage();
            for(int i=0;i<targetMoveCount;i++)
            {
                int packed=moveHistory[i];int loc=DecodeMoveLocation(packed);
                if(!IsValidPackedMove(packed)||(loc<0&&loc!=PASS)||loc>=AREA)return false;
            }

            for(int i=0;i<AREA;i++){board[i]=EMPTY;previousBoard1[i]=EMPTY;previousBoard2[i]=EMPTY;}
            for(int i=0;i<5;i++){recentMoveLoc[i]=NONE;recentMovePla[i]=EMPTY;}
            // Only the prefix that can be observed by this rebuild is
            // overwritten.  The old implementation cleared all 2049 history
            // slots even for a short undo, creating an avoidable main-thread
            // spike.  positionHistoryCount remains the authoritative bound;
            // untouched tail entries are never consulted.
            int clearHistoryCount=Mathf.Clamp(Mathf.Max(positionHistoryCount,
                targetMoveCount+1),0,MAX_POSITION_HISTORY);
            lastRebuildHistoryEntriesCleared=clearHistoryCount;
            for(int i=0;i<clearHistoryCount;i++)
            {
                hash0[i]=0;hash1[i]=0;hash2[i]=0;hash3[i]=0;
                historyStoneCount[i]=-1;
            }
            historyStoneCountFrequencyValid=false;
            positionHistoryCount=0;blackCaptures=0;whiteCaptures=0;consecutivePasses=0;koLoc=NONE;lastMove=NONE;winner=EMPTY;gameState=STATE_PLAYING;finalBlackArea=0;finalWhiteArea=0;finalWhiteMinusBlackScore=0f;
            sideToMove=starter==BLACK||starter==WHITE?starter:BLACK;
            if(handicapStones>0)
            {
                PlaceHandicapStones(handicapStones);sideToMove=WHITE;
                Copy(board,previousBoard1);Copy(board,previousBoard2);
            }
            ComputeHash();AppendHash();moveCount=0;
            for(int i=0;i<targetMoveCount;i++)
            {
                int packed=moveHistory[i];
                if(!ReplayMove(DecodeMoveLocation(packed),DecodeMovePlayer(packed)))return false;
            }
            gameState=STATE_PLAYING;winner=EMPTY;finalBlackArea=0;finalWhiteArea=0;finalWhiteMinusBlackScore=0f;
            return true;
        }

        public bool TryRebuildPositionToMoveCount(int targetMoveCount)
        {
            transactionalRebuildAttempts++;
            if(!CaptureRebuildTransaction())
            {
                transactionalRebuildFailures++;
                return false;
            }
            if(RebuildPositionToMoveCount(targetMoveCount))return true;
            transactionalRebuildFailures++;
            RestoreRebuildTransaction();
            transactionalRebuildRestores++;
            return false;
        }

        private bool CaptureRebuildTransaction()
        {
            if(moveHistory==null||moveHistory.Length<MAX_MOVES)
                moveHistory=new int[MAX_MOVES];
            if(hash0==null||hash0.Length<MAX_POSITION_HISTORY||
                hash1==null||hash1.Length<MAX_POSITION_HISTORY||
                hash2==null||hash2.Length<MAX_POSITION_HISTORY||
                hash3==null||hash3.Length<MAX_POSITION_HISTORY)
            {
                hash0=new int[MAX_POSITION_HISTORY];hash1=new int[MAX_POSITION_HISTORY];
                hash2=new int[MAX_POSITION_HISTORY];hash3=new int[MAX_POSITION_HISTORY];
            }
            if(historyStoneCount==null||historyStoneCount.Length<MAX_POSITION_HISTORY)
                historyStoneCount=new int[MAX_POSITION_HISTORY];
            if(historyStoneCountFrequency==null||historyStoneCountFrequency.Length<AREA+1)
            {
                historyStoneCountFrequency=new int[AREA+1];
                historyStoneCountFrequencyValid=false;
            }
            if(rebuildMoveHistorySnapshot==null||
                rebuildMoveHistorySnapshot.Length<MAX_MOVES)
                rebuildMoveHistorySnapshot=new int[MAX_MOVES];
            if(rebuildHash0Snapshot==null||
                rebuildHash0Snapshot.Length<MAX_POSITION_HISTORY)
            {
                rebuildHash0Snapshot=new int[MAX_POSITION_HISTORY];
                rebuildHash1Snapshot=new int[MAX_POSITION_HISTORY];
                rebuildHash2Snapshot=new int[MAX_POSITION_HISTORY];
                rebuildHash3Snapshot=new int[MAX_POSITION_HISTORY];
                rebuildStoneCountSnapshot=new int[MAX_POSITION_HISTORY];
            }
            if(rebuildStoneCountFrequencySnapshot==null||
                rebuildStoneCountFrequencySnapshot.Length<AREA+1)
                rebuildStoneCountFrequencySnapshot=new int[AREA+1];
            if(rebuildMoveHistorySnapshot==null||rebuildHash0Snapshot==null||
                rebuildHash1Snapshot==null||rebuildHash2Snapshot==null||
                rebuildHash3Snapshot==null||rebuildStoneCountSnapshot==null||
                rebuildStoneCountFrequencySnapshot==null)
                return false;
            Copy(board,rebuildBoardSnapshot);
            Copy(previousBoard1,rebuildPreviousBoard1Snapshot);
            Copy(previousBoard2,rebuildPreviousBoard2Snapshot);
            for(int i=0;i<5;i++)
            {
                rebuildRecentMoveLocSnapshot[i]=recentMoveLoc[i];
                rebuildRecentMovePlaSnapshot[i]=recentMovePla[i];
            }
            for(int i=0;i<MAX_MOVES;i++)rebuildMoveHistorySnapshot[i]=moveHistory[i];
            for(int i=0;i<MAX_POSITION_HISTORY;i++)
            {
                rebuildHash0Snapshot[i]=hash0[i];rebuildHash1Snapshot[i]=hash1[i];
                rebuildHash2Snapshot[i]=hash2[i];rebuildHash3Snapshot[i]=hash3[i];
                rebuildStoneCountSnapshot[i]=historyStoneCount[i];
            }
            for(int i=0;i<=AREA;i++)
                rebuildStoneCountFrequencySnapshot[i]=historyStoneCountFrequency[i];
            rebuildSnapshotStoneCountFrequencyValid=historyStoneCountFrequencyValid;
            rebuildSnapshotPositionHistoryCount=positionHistoryCount;
            rebuildSnapshotSideToMove=sideToMove;rebuildSnapshotMoveCount=moveCount;
            rebuildSnapshotConsecutivePasses=consecutivePasses;
            rebuildSnapshotBlackCaptures=blackCaptures;
            rebuildSnapshotWhiteCaptures=whiteCaptures;
            rebuildSnapshotGameState=gameState;rebuildSnapshotWinner=winner;
            rebuildSnapshotLastMove=lastMove;rebuildSnapshotKoLoc=koLoc;
            rebuildSnapshotFinalBlackArea=finalBlackArea;
            rebuildSnapshotFinalWhiteArea=finalWhiteArea;
            rebuildSnapshotFinalScore=finalWhiteMinusBlackScore;
            rebuildSnapshotAction=lastActionText;
            rebuildSnapshotHistoryStoneCountVersion=historyStoneCountVersion;
            rebuildSnapshotH0=h0;rebuildSnapshotH1=h1;
            rebuildSnapshotH2=h2;rebuildSnapshotH3=h3;
            rebuildSnapshotCurrentStoneCount=currentStoneCount;
            return true;
        }

        private void RestoreRebuildTransaction()
        {
            Copy(rebuildBoardSnapshot,board);
            Copy(rebuildPreviousBoard1Snapshot,previousBoard1);
            Copy(rebuildPreviousBoard2Snapshot,previousBoard2);
            for(int i=0;i<5;i++)
            {
                recentMoveLoc[i]=rebuildRecentMoveLocSnapshot[i];
                recentMovePla[i]=rebuildRecentMovePlaSnapshot[i];
            }
            for(int i=0;i<MAX_MOVES;i++)moveHistory[i]=rebuildMoveHistorySnapshot[i];
            for(int i=0;i<MAX_POSITION_HISTORY;i++)
            {
                hash0[i]=rebuildHash0Snapshot[i];hash1[i]=rebuildHash1Snapshot[i];
                hash2[i]=rebuildHash2Snapshot[i];hash3[i]=rebuildHash3Snapshot[i];
                historyStoneCount[i]=rebuildStoneCountSnapshot[i];
            }
            for(int i=0;i<=AREA;i++)
                historyStoneCountFrequency[i]=rebuildStoneCountFrequencySnapshot[i];
            positionHistoryCount=rebuildSnapshotPositionHistoryCount;
            sideToMove=rebuildSnapshotSideToMove;moveCount=rebuildSnapshotMoveCount;
            consecutivePasses=rebuildSnapshotConsecutivePasses;
            blackCaptures=rebuildSnapshotBlackCaptures;
            whiteCaptures=rebuildSnapshotWhiteCaptures;
            gameState=rebuildSnapshotGameState;winner=rebuildSnapshotWinner;
            lastMove=rebuildSnapshotLastMove;koLoc=rebuildSnapshotKoLoc;
            finalBlackArea=rebuildSnapshotFinalBlackArea;
            finalWhiteArea=rebuildSnapshotFinalWhiteArea;
            finalWhiteMinusBlackScore=rebuildSnapshotFinalScore;
            lastActionText=rebuildSnapshotAction;
            historyStoneCountVersion=rebuildSnapshotHistoryStoneCountVersion;
            h0=rebuildSnapshotH0;h1=rebuildSnapshotH1;
            h2=rebuildSnapshotH2;h3=rebuildSnapshotH3;
            currentStoneCount=rebuildSnapshotCurrentStoneCount;
            // Restore the exact derived index as part of the transaction too.
            // A failed replay must be observationally identical to entering
            // the method, including the fast superko-reject cache.
            historyStoneCountFrequencyValid=rebuildSnapshotStoneCountFrequencyValid;
            InvalidateMoveMaskCache();
        }

        private bool ReplayMove(int loc,int pla)
        {
            if(pla!=sideToMove||gameState!=STATE_PLAYING||moveCount>=MAX_MOVES||positionHistoryCount>=MAX_POSITION_HISTORY)return false;
            if(loc==PASS)
            {
                Copy(previousBoard1,previousBoard2);Copy(board,previousBoard1);PushRecentMove(pla,PASS);sideToMove=-pla;consecutivePasses++;lastMove=PASS;koLoc=NONE;RecordMove(pla,PASS);moveCount++;ComputeHash();AppendHash();return true;
            }
            if(loc<0||loc>=AREA||board[loc]!=EMPTY||loc==koLoc)return false;
            // Keep the same KataGo history convention as TryPlayInternal:
            // previousBoard1 is the position immediately before this move,
            // and previousBoard2 is the position before that.  Replay is used
            // by Undo, so copying after applying the move would shift both
            // history planes forward by one ply and change neural input for
            // an otherwise identical position.
            Copy(board,backup);Copy(previousBoard1,previousBoard2);Copy(backup,previousBoard1);
            board[loc]=pla;int captured=0;int single=NONE;int n=Left(loc);if(n>=0&&board[n]==-pla)captured+=CaptureDead(n,-pla,ref single);n=Right(loc);if(n>=0&&board[n]==-pla)captured+=CaptureDead(n,-pla,ref single);n=Up(loc);if(n>=0&&board[n]==-pla)captured+=CaptureDead(n,-pla,ref single);n=Down(loc);if(n>=0&&board[n]==-pla)captured+=CaptureDead(n,-pla,ref single);
            int ownSize=CollectGroup(loc,pla);int ownLiberties=CountLiberties();int selfRemoved=0;
            if(ownLiberties==0)
            {
                if(ownSize==1||!multiStoneSuicideLegal)return false;
                selfRemoved=ownSize;for(int i=0;i<ownSize;i++)board[group[i]]=EMPTY;
            }
            ComputeHash();if(positionalSuperko&&HashSeen(h0,h1,h2,h3))return false;
            if(pla==BLACK){blackCaptures+=captured;whiteCaptures+=selfRemoved;}else{whiteCaptures+=captured;blackCaptures+=selfRemoved;}
            koLoc=NONE;if(captured==1&&selfRemoved==0&&board[loc]==pla){int size=CollectGroup(loc,pla);if(size==1&&CountLiberties()==1)koLoc=single;}
            PushRecentMove(pla,loc);sideToMove=-pla;consecutivePasses=0;lastMove=loc;RecordMove(pla,loc);moveCount++;AppendHash();return true;
        }

        private void PlaceHandicapStones(int count)
        {
            if(count>=2){SetStone(3,15);SetStone(15,3);}if(count>=3)SetStone(15,15);if(count>=4)SetStone(3,3);if(count==5||count==7||count==9)SetStone(9,9);if(count>=6){SetStone(3,9);SetStone(15,9);}if(count>=8){SetStone(9,3);SetStone(9,15);}
        }
        private void AppendHash()
        {
            if(positionHistoryCount>=MAX_POSITION_HISTORY)return;
            if(historyStoneCountVersion>0)
            {
                EnsureHistoryStoneCountFrequency();
                if(historyStoneCountFrequencyValid&&currentStoneCount>=0&&currentStoneCount<=AREA)
                    historyStoneCountFrequency[currentStoneCount]++;
            }
            hash0[positionHistoryCount]=h0;hash1[positionHistoryCount]=h1;
            hash2[positionHistoryCount]=h2;hash3[positionHistoryCount]=h3;
            historyStoneCount[positionHistoryCount]=currentStoneCount;
            positionHistoryCount++;
        }
        private bool EnsureHistoryStoneCountStorage()
        {
            if(historyStoneCount!=null&&historyStoneCount.Length>=MAX_POSITION_HISTORY)return false;
            historyStoneCount=new int[MAX_POSITION_HISTORY];historyStoneCountVersion=0;
            historyStoneCountFrequencyValid=false;return true;
        }
        private bool EnsureMoveHistoryStorage()
        {
            if(moveHistory!=null&&moveHistory.Length>=MAX_MOVES)return false;
            moveHistory=new int[MAX_MOVES];return true;
        }
        private bool HashSeen(int a,int b,int c,int d)
        {
            if(historyStoneCountVersion>0&&historyStoneCountFrequencyValid&&
                (currentStoneCount<0||currentStoneCount>AREA||
                 historyStoneCountFrequency[currentStoneCount]==0))return false;
            for(int i=0;i<positionHistoryCount;i++)
                if(hash0[i]==a&&hash1[i]==b&&hash2[i]==c&&hash3[i]==d)return true;
            return false;
        }

        private void EnsureHistoryStoneCountFrequency()
        {
            if(historyStoneCountFrequency==null||historyStoneCountFrequency.Length<AREA+1)
            {
                historyStoneCountFrequency=new int[AREA+1];
                historyStoneCountFrequencyValid=false;
            }
            if(historyStoneCountFrequencyValid)return;
            for(int i=0;i<=AREA;i++)historyStoneCountFrequency[i]=0;
            if(historyStoneCount==null||historyStoneCountVersion<=0)
            {
                historyStoneCountFrequencyValid=false;return;
            }
            int count=Mathf.Clamp(positionHistoryCount,0,MAX_POSITION_HISTORY);
            bool complete=true;
            for(int i=0;i<count;i++)
            {
                int stones=historyStoneCount[i];
                if(stones<0||stones>AREA){complete=false;continue;}
                historyStoneCountFrequency[stones]++;
            }
            historyStoneCountFrequencyValid=complete;
            historyStoneCountIndexRebuilds++;
        }
        private void ComputeHash()
        {
            int a=-2128831035,b=-1640531527,c=-2048144789,d=-1028477387;int stoneCount=0;for(int i=0;i<AREA;i++){int v=board[i]+2;if(board[i]!=EMPTY)stoneCount++;a=(a^(v*257+i))*16777619;b=(b+v*65537+i*17)*0x27D4EB2D;c=(c^(v*131071+i*31))*0x165667B1;d=(d+(v*8191^i*127))*0x1B873593;}h0=a;h1=b;h2=c;h3=d;currentStoneCount=stoneCount;
        }
        private void SetStone(int x,int y){board[y*SIZE+x]=BLACK;} private int Left(int l){return l%SIZE==0?-1:l-1;} private int Right(int l){return l%SIZE==SIZE-1?-1:l+1;} private int Up(int l){return l<SIZE?-1:l-SIZE;} private int Down(int l){return l>=AREA-SIZE?-1:l+SIZE;}
        private void Copy(int[] s,int[] t){for(int i=0;i<AREA;i++)t[i]=s[i];}
        private string Coordinate(int loc){int x=loc%SIZE,y=loc/SIZE;string cols="ABCDEFGHJKLMNOPQRST";return cols.Substring(x,1)+(SIZE-y);}
        private int LocalPlayerId(){VRCPlayerApi p=Networking.LocalPlayer;return Utilities.IsValid(p)?p.playerId:0;}
        private string LocalPlayerName(){VRCPlayerApi p=Networking.LocalPlayer;return Utilities.IsValid(p)?p.displayName:"Local Player";}
        private bool TakeOwnership()
        {
            VRCPlayerApi p=Networking.LocalPlayer;
            if(!Utilities.IsValid(p))return true;
            if(!Networking.IsOwner(gameObject))Networking.SetOwner(p,gameObject);
            // The game object is the authoritative command domain.  Runtime
            // readers, settings and telemetry are separate behaviours whose
            // ownership callbacks settle asynchronously; they must not make a
            // valid seat/lobby command fail just because an attached worker is
            // still owned by the previous player.
            if(aiController!=null&&!Networking.IsOwner(aiController.gameObject))Networking.SetOwner(p,aiController.gameObject);
            if(aiController!=null&&aiController.alternateReader!=null&&
                !Networking.IsOwner(aiController.alternateReader.gameObject))
                Networking.SetOwner(p,aiController.alternateReader.gameObject);
            if(aiController!=null&&aiController.tertiaryReader!=null&&
                !Networking.IsOwner(aiController.tertiaryReader.gameObject))
                Networking.SetOwner(p,aiController.tertiaryReader.gameObject);
            if(aiSettings!=null&&!Networking.IsOwner(aiSettings.gameObject))
                Networking.SetOwner(p,aiSettings.gameObject);
            // Resolved profiles are local views. Ownership is held by the one
            // GoAiSettings component above, not by two independent profiles.
            if(telemetry!=null&&!Networking.IsOwner(telemetry.gameObject))Networking.SetOwner(p,telemetry.gameObject);
            return Networking.IsOwner(gameObject);
        }
        private void PublishAudio(int type){audioEventType=type;audioEventRevision++;if(audioEventRevision==0)audioEventRevision=1;lastAudioEventRevision=audioEventRevision;PlayAudioEvent(type);}
        private void PlayAudioEvent(int type)
        {
            if(audioSource==null)return;
            AudioClip clip=null;
            if(type==AUDIO_PLACE)clip=placeClip;
            else if(type==AUDIO_CAPTURE)clip=captureClip;
            else if(type==AUDIO_PASS)clip=passClip;
            else if(type==AUDIO_GAME_END)clip=gameEndClip;
            else if(type==AUDIO_RESET)clip=resetClip;
            if(clip!=null)audioSource.PlayOneShot(clip);
        }
        private void Refresh()
        {
            if(aiController!=null)aiController.WakeForStateChange();
            if(boardPool!=null&&!IsMoveMaskCacheCompleteForCurrentRevision())
                boardPool.WakeMoveMaskTable(tableIndex);
            int reason=pendingBoardRefreshReason;
            pendingBoardRefreshReason=GoBoardView.REFRESH_OTHER;
            if(view!=null)view.RefreshForReason(reason);
            if(ui!=null)ui.RefreshNow();
            if(telemetry!=null)telemetry.RefreshNow();
        }
    }
}
