using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only probe for Xiangqi-family undo/draw interaction. Each
    /// mutation phase runs on a separate Udon Update so the VM watchdog cannot
    /// confuse a legitimate history rebuild with one monolithic test event.
    /// </summary>
    public sealed class GoUndoDrawProbe : UdonSharpBehaviour
    {
        public GoGame game;
        public GoAiController controller;
        public GoBoardView view;
        public bool probeFinished;
        public bool pvAiUndoPassed;
        public bool preUndoHistoryPassed;
        public bool postUndoHistoryPassed;
        public bool pvAiUndoHistoryPassed;
        public int postPrevious1At0;
        public int postPrevious1At1;
        public int postPrevious1At2;
        public int postPrevious2At0;
        public int postPrevious2At1;
        public int postPrevious2At2;
        public int postHistory1Mismatch;
        public int postHistory1MismatchValue;
        public int postHistory2Mismatch;
        public int postHistory2MismatchValue;
        public bool pvpUndoConsentPassed;
        public bool pvpDrawConsentPassed;
        public bool transactionalUndoPassed;
        public bool undoNoCapturePresentationPassed;
        public bool transactionalRebuildReturned;
        public int transactionalMismatchCount;
        public string failure;

        private int phase;
        private bool running;
        private int[] transactionBoard=new int[GoGame.AREA];
        private int[] transactionPrevious1=new int[GoGame.AREA];
        private int[] transactionPrevious2=new int[GoGame.AREA];
        private int[] transactionRecentLoc=new int[5];
        private int[] transactionRecentPla=new int[5];
        private int[] transactionMoves=new int[GoGame.MAX_MOVES];
        private int[] transactionHash0=new int[GoGame.MAX_POSITION_HISTORY];
        private int[] transactionHash1=new int[GoGame.MAX_POSITION_HISTORY];
        private int[] transactionHash2=new int[GoGame.MAX_POSITION_HISTORY];
        private int[] transactionHash3=new int[GoGame.MAX_POSITION_HISTORY];
        private int[] transactionStoneCounts=new int[GoGame.MAX_POSITION_HISTORY];
        private int transactionMoveCount,transactionPositionHistoryCount,transactionSide;
        private int transactionPasses,transactionBlackCaptures,transactionWhiteCaptures;
        private int transactionKo,transactionLastMove,transactionState,transactionWinner;
        private int transactionBlackArea,transactionWhiteArea,transactionHistoryVersion;
        private float transactionScore;

        public void RunUndoDrawProbe()
        {
            probeFinished=false;pvAiUndoPassed=false;preUndoHistoryPassed=false;postUndoHistoryPassed=false;pvAiUndoHistoryPassed=false;postPrevious1At0=0;postPrevious1At1=0;postPrevious1At2=0;postPrevious2At0=0;postPrevious2At1=0;postPrevious2At2=0;postHistory1Mismatch=-1;postHistory1MismatchValue=0;postHistory2Mismatch=-1;postHistory2MismatchValue=0;pvpUndoConsentPassed=false;
            pvpDrawConsentPassed=false;failure="";phase=0;running=true;
            transactionalUndoPassed=false;transactionalRebuildReturned=false;
            undoNoCapturePresentationPassed=false;
            transactionalMismatchCount=0;
            if(game==null||controller==null){Finish("undo/draw probe references are incomplete");return;}
            controller.autoStart=false;
        }

        public void Update()
        {
            if(!running||probeFinished||game==null)return;
            if(phase==0)
            {
                game.SetMatchMode(1);game.RequestForceReset();game.SetBlackStarts();game.RequestStartMatch();
                phase=1;return;
            }
            if(phase==1)
            {
                bool blackMove=game.TryPlay(0);bool whiteMove=game.TryPlayFromAI(1);bool secondBlackMove=game.TryPlay(2);
                preUndoHistoryPassed=MatchesSnapshot(game.previousBoard1,0,GoGame.BLACK,1,GoGame.WHITE,-1,GoGame.EMPTY)&&
                    MatchesSnapshot(game.previousBoard2,0,GoGame.BLACK,-1,GoGame.EMPTY,-1,GoGame.EMPTY);
                int captureAnimationsBeforeUndo=view==null?0:view.captureAnimationStartCount;
                game.RequestUndo();
                undoNoCapturePresentationPassed=view!=null&&
                    view.captureAnimationStartCount==captureAnimationsBeforeUndo&&
                    view.lastRefreshReason==GoBoardView.REFRESH_UNDO&&
                    !view.IsCaptureAnimating(2);
                pvAiUndoPassed=blackMove&&whiteMove&&secondBlackMove&&game.moveCount==2&&game.sideToMove==GoGame.BLACK&&
                    game.board[0]==GoGame.BLACK&&game.board[1]==GoGame.WHITE&&game.board[2]==GoGame.EMPTY&&game.positionHistoryCount==3&&
                    game.gameState==GoGame.STATE_PLAYING&&game.matchStarted;
                postPrevious1At0=game.previousBoard1[0];postPrevious1At1=game.previousBoard1[1];postPrevious1At2=game.previousBoard1[2];
                postPrevious2At0=game.previousBoard2[0];postPrevious2At1=game.previousBoard2[1];postPrevious2At2=game.previousBoard2[2];
                // After undoing only the third ply, the current position is
                // B@0/W@1.  The immediate history is the position before the
                // second ply (B@0), followed by the empty initial position.
                postHistory1Mismatch=FindSnapshotMismatch(game.previousBoard1,0,GoGame.BLACK,-1,GoGame.EMPTY,-1,GoGame.EMPTY);
                postHistory2Mismatch=FindSnapshotMismatch(game.previousBoard2,-1,GoGame.EMPTY,-1,GoGame.EMPTY,-1,GoGame.EMPTY);
                postHistory1MismatchValue=postHistory1Mismatch<0?0:game.previousBoard1[postHistory1Mismatch];
                postHistory2MismatchValue=postHistory2Mismatch<0?0:game.previousBoard2[postHistory2Mismatch];
                postUndoHistoryPassed=postHistory1Mismatch<0&&postHistory2Mismatch<0;
                pvAiUndoHistoryPassed=preUndoHistoryPassed&&postUndoHistoryPassed;
                phase=2;return;
            }
            if(phase==2)
            {
                game.SetMatchMode(0);game.RequestForceReset();game.SetBlackStarts();game.ClaimBlack();
                // PvP uses the same explicit pre-match start gate as the UI;
                // exercise consent only after the authoritative match began.
                game.RequestStartMatch();
                bool blackMove=game.TryPlay(0);
                // Single ClientSim has one local seated player. Simulate the
                // authoritative turn hand-off so the real current-player
                // Undo permission path can create the consent offer.
                game.sideToMove=GoGame.BLACK;game.revision++;
                game.RequestUndo();
                bool offered=game.undoOfferSide==GoGame.BLACK&&game.undoOfferMoveCount==1&&
                    game.moveCount==1&&game.board[0]==GoGame.BLACK;
                game.RequestUndo();
                pvpUndoConsentPassed=blackMove&&offered&&game.undoOfferSide==GoGame.BLACK&&
                    game.moveCount==1&&game.board[0]==GoGame.BLACK&&game.gameState==GoGame.STATE_PLAYING;
                phase=3;return;
            }
            if(phase==3)
            {
                game.RequestForceReset();game.SetBlackStarts();game.ClaimBlack();game.RequestStartMatch();game.OfferOrAcceptDraw();
                bool offered=game.drawOfferSide==GoGame.BLACK&&game.drawOfferMoveCount==0&&
                    game.gameState==GoGame.STATE_PLAYING;
                game.OfferOrAcceptDraw();
                pvpDrawConsentPassed=offered&&game.drawOfferSide==GoGame.BLACK&&game.gameState==GoGame.STATE_PLAYING;
                phase=4;return;
            }
            if(phase==4)
            {
                game.SetMatchMode(GoAiController.MODE_AIVAI);game.RequestForceReset();
                game.SetBlackStarts();game.RequestStartMatch();
                bool built=game.TryPlayFromAI(0)&&game.TryPlayFromAI(1)&&
                    game.TryPlayFromAI(2);
                if(!built){Finish("transactional fixture could not be built");return;}
                // Syntactically valid packed White move at loc 0, but illegal
                // during replay because Black already occupies loc 0.
                game.moveHistory[1]=514;
                CaptureTransactionState();
                transactionalRebuildReturned=game.TryRebuildPositionToMoveCount(3);
                transactionalMismatchCount=CountTransactionMismatches();
                transactionalUndoPassed=!transactionalRebuildReturned&&
                    transactionalMismatchCount==0&&game.transactionalRebuildFailures>0&&
                    game.transactionalRebuildRestores>0;
                if(!pvAiUndoPassed)failure+=" pvai-undo";
                if(!preUndoHistoryPassed)failure+=" pre-history";
                if(!postUndoHistoryPassed)failure+=" post-history";
                if(!pvAiUndoHistoryPassed)failure+=" pvai-undo-history";
                if(!pvpUndoConsentPassed)failure+=" pvp-undo-consent";
                if(!pvpDrawConsentPassed)failure+=" pvp-draw-consent";
                if(!transactionalUndoPassed)failure+=" transactional-rebuild";
                if(!undoNoCapturePresentationPassed)failure+=" undo-capture";
                Finish(failure.Length==0?"":failure);return;
            }
            Finish("undo/draw probe reached an invalid phase");
        }

        private void CaptureTransactionState()
        {
            for(int i=0;i<GoGame.AREA;i++)
            {transactionBoard[i]=game.board[i];transactionPrevious1[i]=game.previousBoard1[i];transactionPrevious2[i]=game.previousBoard2[i];}
            for(int i=0;i<5;i++)
            {transactionRecentLoc[i]=game.recentMoveLoc[i];transactionRecentPla[i]=game.recentMovePla[i];}
            for(int i=0;i<GoGame.MAX_MOVES;i++)transactionMoves[i]=game.moveHistory[i];
            for(int i=0;i<GoGame.MAX_POSITION_HISTORY;i++)
            {
                transactionHash0[i]=game.hash0[i];transactionHash1[i]=game.hash1[i];
                transactionHash2[i]=game.hash2[i];transactionHash3[i]=game.hash3[i];
                transactionStoneCounts[i]=game.historyStoneCount[i];
            }
            transactionMoveCount=game.moveCount;transactionPositionHistoryCount=game.positionHistoryCount;
            transactionSide=game.sideToMove;transactionPasses=game.consecutivePasses;
            transactionBlackCaptures=game.blackCaptures;transactionWhiteCaptures=game.whiteCaptures;
            transactionKo=game.koLoc;transactionLastMove=game.lastMove;
            transactionState=game.gameState;transactionWinner=game.winner;
            transactionBlackArea=game.finalBlackArea;transactionWhiteArea=game.finalWhiteArea;
            transactionScore=game.finalWhiteMinusBlackScore;
            transactionHistoryVersion=game.historyStoneCountVersion;
        }

        private int CountTransactionMismatches()
        {
            int mismatches=0;
            for(int i=0;i<GoGame.AREA;i++)
            {
                if(transactionBoard[i]!=game.board[i])mismatches++;
                if(transactionPrevious1[i]!=game.previousBoard1[i])mismatches++;
                if(transactionPrevious2[i]!=game.previousBoard2[i])mismatches++;
            }
            for(int i=0;i<5;i++)
            {
                if(transactionRecentLoc[i]!=game.recentMoveLoc[i])mismatches++;
                if(transactionRecentPla[i]!=game.recentMovePla[i])mismatches++;
            }
            for(int i=0;i<GoGame.MAX_MOVES;i++)
                if(transactionMoves[i]!=game.moveHistory[i])mismatches++;
            for(int i=0;i<GoGame.MAX_POSITION_HISTORY;i++)
            {
                if(transactionHash0[i]!=game.hash0[i])mismatches++;
                if(transactionHash1[i]!=game.hash1[i])mismatches++;
                if(transactionHash2[i]!=game.hash2[i])mismatches++;
                if(transactionHash3[i]!=game.hash3[i])mismatches++;
                if(transactionStoneCounts[i]!=game.historyStoneCount[i])mismatches++;
            }
            if(transactionMoveCount!=game.moveCount)mismatches++;
            if(transactionPositionHistoryCount!=game.positionHistoryCount)mismatches++;
            if(transactionSide!=game.sideToMove)mismatches++;
            if(transactionPasses!=game.consecutivePasses)mismatches++;
            if(transactionBlackCaptures!=game.blackCaptures)mismatches++;
            if(transactionWhiteCaptures!=game.whiteCaptures)mismatches++;
            if(transactionKo!=game.koLoc)mismatches++;
            if(transactionLastMove!=game.lastMove)mismatches++;
            if(transactionState!=game.gameState)mismatches++;
            if(transactionWinner!=game.winner)mismatches++;
            if(transactionBlackArea!=game.finalBlackArea)mismatches++;
            if(transactionWhiteArea!=game.finalWhiteArea)mismatches++;
            if(transactionScore!=game.finalWhiteMinusBlackScore)mismatches++;
            if(transactionHistoryVersion!=game.historyStoneCountVersion)mismatches++;
            return mismatches;
        }

        private bool MatchesSnapshot(int[] snapshot,int loc0,int color0,int loc1,int color1,int loc2,int color2)
        {
            return FindSnapshotMismatch(snapshot,loc0,color0,loc1,color1,loc2,color2)<0;
        }

        private int FindSnapshotMismatch(int[] snapshot,int loc0,int color0,int loc1,int color1,int loc2,int color2)
        {
            if(snapshot==null||snapshot.Length<GoGame.AREA)return -99;
            for(int i=0;i<GoGame.AREA;i++)
            {
                int expected=GoGame.EMPTY;
                if(i==loc0)expected=color0;
                else if(i==loc1)expected=color1;
                else if(i==loc2)expected=color2;
                if(snapshot[i]!=expected)return i;
            }
            return -1;
        }

        private void Finish(string message)
        {
            failure=message;probeFinished=true;running=false;
        }
    }
}
