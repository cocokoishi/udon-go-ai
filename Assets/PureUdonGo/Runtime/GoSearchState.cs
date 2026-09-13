using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Allocation-free, non-networked Go position used by MCTS rollouts.
    /// It mirrors the production GoGame rules but never takes ownership,
    /// refreshes UI, or requests serialization.
    /// </summary>
    public sealed class GoSearchState : UdonSharpBehaviour
    {
        public int[] board=new int[GoGame.AREA];
        public int[] previousBoard1=new int[GoGame.AREA];
        public int[] previousBoard2=new int[GoGame.AREA];
        public int[] recentMoveLoc=new int[5];
        public int[] recentMovePla=new int[5];
        public int[] hash0=new int[GoGame.MAX_POSITION_HISTORY];
        public int[] hash1=new int[GoGame.MAX_POSITION_HISTORY];
        public int[] hash2=new int[GoGame.MAX_POSITION_HISTORY];
        public int[] hash3=new int[GoGame.MAX_POSITION_HISTORY];
        public int[] historyStoneCount=new int[GoGame.MAX_POSITION_HISTORY];
        // Local exact index keyed by historical stone count. It is copied once
        // with the root prefix and incrementally updated for appended
        // simulation moves, allowing superko probes to skip impossible hash
        // comparisons without weakening collision checks.
        public int[] historyStoneCountFrequency=new int[GoGame.AREA+1];
        public bool historyStoneCountFrequencyValid;
        public int historyStoneCountIndexRebuilds;
        public int historyStoneCountVersion;
        // Exact positive-match index for superko. Heads use index+1 so zero
        // remains the empty sentinel; entries are pushed/popped with the
        // simulation history and never copied per visit.
        public int[] historyBucketHead=new int[GoGame.AREA+1];
        public int[] historyBucketNext=new int[GoGame.MAX_POSITION_HISTORY];
        public int historyEntriesExamined;
        public int historyQueryCount;
        public int historyBucketHitCount;
        public int historyBucketMissCount;
        public int historyFullScanCount;
        public int historyMaxEntriesExamined;
        public int historyBucketRestoreRejected;
        public int positionHistoryCount;
        public int sideToMove=GoGame.BLACK;
        public int moveCount;
        public int consecutivePasses;
        public int blackCaptures;
        public int whiteCaptures;
        public int gameState=GoGame.STATE_PLAYING;
        public int winner=GoGame.EMPTY;
        public int lastMove=GoGame.NONE;
        public int koLoc=GoGame.NONE;
        public int handicapStones;
        public int komiTimes2=15;
        public bool positionalSuperko=true;
        public bool areaScoring=true;
        public bool multiStoneSuicideLegal=true;
        public int currentHash0;
        public int currentHash1;
        public int currentHash2;
        public int currentHash3;
        public int currentStoneCount;
        public int finalBlackArea;
        public int finalWhiteArea;
        public float finalWhiteMinusBlackScore;
        public int lastRootStateCopiedInts;
        public int lastRootHistoryCopiedEntries;

        private int[] backup=new int[GoGame.AREA];
        private int[] group=new int[GoGame.AREA];
        private int[] queue=new int[GoGame.AREA];
        private int[] visitedStamp=new int[GoGame.AREA];
        private int[] libertyStamp=new int[GoGame.AREA];
        private bool[] regionVisited=new bool[GoGame.AREA];
        private bool historyStoneCountsTrusted;
        private int groupStamp=1;
        private int currentLibertyCount;
        private bool probeActive;
        private int probeId=1;
        private int probeChangedCount;
        private int[] probeMark=new int[GoGame.AREA];
        private int[] probeChangedLoc=new int[GoGame.AREA];
        private int[] probeOldValue=new int[GoGame.AREA];
        // A leaf's legal/superko mask is queried once by the feature path and
        // again by MCTS expansion. Cache the exact combined result while the
        // simulation position is unchanged so those two consumers do not
        // repeat the same group/hash probes.
        private int[] moveMaskCache=new int[GoGame.AREA];
        private bool[] moveMaskComputed=new bool[GoGame.AREA];
        private bool moveMaskCacheValid;
        private bool moveMaskProbe;

        public int GetEstimatedAllocatedArrayBytes()
        {
            int integers=board.Length+previousBoard1.Length+previousBoard2.Length+
                recentMoveLoc.Length+recentMovePla.Length+hash0.Length+hash1.Length+
                hash2.Length+hash3.Length+historyStoneCount.Length+backup.Length+
                group.Length+queue.Length+visitedStamp.Length+libertyStamp.Length+
                probeMark.Length+probeChangedLoc.Length+probeOldValue.Length+
                moveMaskCache.Length+historyStoneCountFrequency.Length+
                historyBucketHead.Length+historyBucketNext.Length;
            int booleans=regionVisited.Length+moveMaskComputed.Length;
            return integers*4+booleans;
        }

        public void CopyFromGame(GoGame game)
        {
            if(historyStoneCount==null||historyStoneCount.Length<GoGame.MAX_POSITION_HISTORY)historyStoneCount=new int[GoGame.MAX_POSITION_HISTORY];
            for(int i=0;i<GoGame.AREA;i++){board[i]=game.board[i];previousBoard1[i]=game.previousBoard1[i];previousBoard2[i]=game.previousBoard2[i];}
            for(int i=0;i<5;i++){recentMoveLoc[i]=game.recentMoveLoc[i];recentMovePla[i]=game.recentMovePla[i];}
            historyStoneCountsTrusted=game.historyStoneCount!=null&&game.historyStoneCount.Length>=GoGame.MAX_POSITION_HISTORY&&game.historyStoneCountVersion>0;
            int historyCount=Mathf.Clamp(game.positionHistoryCount,0,GoGame.MAX_POSITION_HISTORY);
            for(int i=0;i<=GoGame.AREA;i++)historyStoneCountFrequency[i]=0;
            for(int i=0;i<=GoGame.AREA;i++)historyBucketHead[i]=0;
            bool completeHistory=historyStoneCountsTrusted;
            for(int i=0;i<historyCount;i++)
            {
                hash0[i]=game.hash0[i];hash1[i]=game.hash1[i];
                hash2[i]=game.hash2[i];hash3[i]=game.hash3[i];
                int stones=historyStoneCountsTrusted?game.historyStoneCount[i]:-1;
                historyStoneCount[i]=stones;
                if(stones<0||stones>GoGame.AREA)completeHistory=false;
                else historyStoneCountFrequency[stones]++;
            }
            historyStoneCountFrequencyValid=completeHistory;
            if(completeHistory)
            {
                for(int i=0;i<historyCount;i++)
                {
                    int stones=historyStoneCount[i];
                    historyBucketNext[i]=historyBucketHead[stones];
                    historyBucketHead[stones]=i+1;
                }
            }
            historyStoneCountIndexRebuilds++;
            historyEntriesExamined=0;
            historyQueryCount=0;historyBucketHitCount=0;
            historyBucketMissCount=0;historyFullScanCount=0;
            historyMaxEntriesExamined=0;
            // Entries at or beyond positionHistoryCount are intentionally not
            // cleared or copied. HashSeen and the stone-count fast reject are
            // bounded by positionHistoryCount, and simulated paths overwrite
            // only their appended tail.
            lastRootHistoryCopiedEntries=historyCount;
            lastRootStateCopiedInts=GoGame.AREA*3+10+historyCount*5+
                (historyStoneCountFrequencyValid?GoGame.AREA+1:0);
            historyStoneCountVersion=game.historyStoneCountVersion;
            positionHistoryCount=game.positionHistoryCount; sideToMove=game.sideToMove; moveCount=game.moveCount; consecutivePasses=game.consecutivePasses;
            blackCaptures=game.blackCaptures; whiteCaptures=game.whiteCaptures; gameState=game.gameState; winner=game.winner; lastMove=game.lastMove;
            koLoc=game.koLoc; handicapStones=game.handicapStones;
            komiTimes2=game.areaScoring?game.GetEffectiveChineseWhiteCompensationTimes2():game.komiTimes2;
            positionalSuperko=game.positionalSuperko;
            areaScoring=game.areaScoring; multiStoneSuicideLegal=game.multiStoneSuicideLegal; finalBlackArea=game.finalBlackArea; finalWhiteArea=game.finalWhiteArea;
            finalWhiteMinusBlackScore=game.finalWhiteMinusBlackScore; ComputeHash();
            InvalidateMoveMaskCache();
        }

        /// <summary>
        /// Rewinds only simulation-appended history entries. The immutable
        /// root prefix stays in place and bucket heads are popped in reverse
        /// order, so a visit never rebuilds the complete history index.
        /// </summary>
        public void RestoreHistoryBucketsToCount(int targetCount)
        {
            targetCount=Mathf.Clamp(targetCount,0,GoGame.MAX_POSITION_HISTORY);
            int current=Mathf.Clamp(positionHistoryCount,0,GoGame.MAX_POSITION_HISTORY);
            // This helper only rewinds simulation-appended entries.  A caller
            // attempting to grow the history would create heads without the
            // corresponding hash/count entries and silently corrupt superko.
            // Leave the state untouched and expose the rejection to the
            // verifier instead of coercing the target upward.
            if(targetCount>current)
            {
                historyBucketRestoreRejected++;
                return;
            }
            if(historyBucketHead==null||historyBucketNext==null||
                !historyStoneCountFrequencyValid)
            {
                positionHistoryCount=targetCount;
                return;
            }
            while(current>targetCount)
            {
                int index=current-1;
                int stones=index<historyStoneCount.Length?historyStoneCount[index]:-1;
                if(stones>=0&&stones<=GoGame.AREA&&historyBucketHead[stones]==index+1)
                {
                    historyBucketHead[stones]=historyBucketNext[index];
                    if(historyStoneCountFrequency[stones]>0)historyStoneCountFrequency[stones]--;
                }
                else
                {
                    // A malformed chain is a cache problem, not a rules
                    // shortcut. Rebuild the bounded prefix once and continue.
                    RebuildHistoryBuckets(targetCount);
                    positionHistoryCount=targetCount;
                    return;
                }
                current--;
            }
            positionHistoryCount=targetCount;
        }

        public void SetHistoryStoneCountTrust(bool trusted)
        {
            historyStoneCountsTrusted=trusted;
            if(!trusted)historyStoneCountFrequencyValid=false;
            else EnsureHistoryStoneCountFrequency();
            InvalidateMoveMaskCache();
        }

        public void InvalidateMoveMaskCache()
        {
            moveMaskCacheValid=false;
        }

        public bool Play(int loc)
        {
            if(loc==GoGame.PASS)return Pass();
            if(!CanAttempt(loc))return false;
            int pla=sideToMove; Copy(board,backup); int oldBlackCaptures=blackCaptures; int oldWhiteCaptures=whiteCaptures; int oldKo=koLoc; int oldStoneCount=currentStoneCount;
            board[loc]=pla; int captured=0; int single=GoGame.NONE;
            int neighbor=Left(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref single);
            neighbor=Right(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref single);
            neighbor=Up(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref single);
            neighbor=Down(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref single);
            int ownSize=CollectGroup(loc,pla); int ownLiberties=CountLiberties(); int selfRemoved=0;
            if(ownLiberties==0)
            {
                if(ownSize==1||!multiStoneSuicideLegal){Reject(oldBlackCaptures,oldWhiteCaptures,oldKo);return false;}
                selfRemoved=ownSize; for(int i=0;i<ownSize;i++)board[group[i]]=GoGame.EMPTY;
            }
            ComputeHash();
            if(positionalSuperko&&HashSeen(currentHash0,currentHash1,currentHash2,currentHash3)){Reject(oldBlackCaptures,oldWhiteCaptures,oldKo);return false;}
            Copy(previousBoard1,previousBoard2); Copy(backup,previousBoard1);
            if(pla==GoGame.BLACK){blackCaptures+=captured;whiteCaptures+=selfRemoved;}else{whiteCaptures+=captured;blackCaptures+=selfRemoved;}
            koLoc=GoGame.NONE;
            if(captured==1&&selfRemoved==0&&board[loc]==pla)
            {
                int size=CollectGroup(loc,pla); if(size==1&&CountLiberties()==1)koLoc=single;
            }
            PushRecentMove(pla,loc); sideToMove=-pla; consecutivePasses=0; lastMove=loc; moveCount++; AppendHash();
            if(IsAtCapacity())FinishByMoveLimit();
            InvalidateMoveMaskCache();
            return true;
        }

        public bool Pass()
        {
            if(gameState!=GoGame.STATE_PLAYING)return false;
            if(IsAtCapacity()){FinishByMoveLimit();return false;}
            int pla=sideToMove; Copy(previousBoard1,previousBoard2); Copy(board,previousBoard1); PushRecentMove(pla,GoGame.PASS);
            sideToMove=-pla; consecutivePasses++; lastMove=GoGame.PASS; koLoc=GoGame.NONE; moveCount++; ComputeHash(); AppendHash();
            if(consecutivePasses>=2)FinishByScore();else if(IsAtCapacity())FinishByMoveLimit();
            InvalidateMoveMaskCache();
            return true;
        }

        public void Resign()
        {
            if(gameState!=GoGame.STATE_PLAYING)return;
            winner=-sideToMove; gameState=GoGame.STATE_RESIGNED; lastMove=GoGame.PASS;
            InvalidateMoveMaskCache();
        }

        public bool IsLegalMove(int loc)
        {
            return (GetMoveMask(loc)&1)!=0;
        }

        public bool IsSuperkoBanned(int loc)
        {
            // Keep this separate from IsLegalMove: the latter is intentionally
            // a full legal-action query, while KataGo's superko mask excludes
            // ordinary illegal actions such as singleton suicide.
            return (GetMoveMask(loc)&2)!=0;
        }

        /// <summary>
        /// Computes ordinary legality and KataGo's separate superko mask in a
        /// single exact probe. Bit 0 is a legal move; bit 1 is superko-banned.
        /// The result is cached for the current simulation position and is
        /// invalidated by every real simulation mutation.
        /// </summary>
        public int GetMoveMask(int loc)
        {
            if(loc<0||loc>=GoGame.AREA)return 0;
            EnsureMoveMaskCache();
            if(moveMaskComputed[loc])return moveMaskCache[loc];
            int mask=0;
            if(CanAttempt(loc))
            {
                int pla=sideToMove;
                int oldKo=koLoc; int oldStoneCount=currentStoneCount;
                int oldHash0=currentHash0; int oldHash1=currentHash1;
                int oldHash2=currentHash2; int oldHash3=currentHash3;
                int ignored=GoGame.NONE; int captured=0; int selfRemoved=0;
                moveMaskProbe=true;
                BeginProbe();SetProbeValue(loc,pla);
                int neighbor=Left(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref ignored);
                neighbor=Right(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref ignored);
                neighbor=Up(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref ignored);
                neighbor=Down(loc); if(neighbor>=0&&board[neighbor]==-pla)captured+=CaptureDead(neighbor,-pla,ref ignored);
                int size=CollectGroup(loc,pla);
                bool ordinaryLegal=true;
                if(CountLiberties()==0)
                {
                    if(size==1||!multiStoneSuicideLegal)ordinaryLegal=false;
                    else
                    {
                        selfRemoved=size;
                        for(int i=0;i<size;i++)SetProbeValue(group[i],GoGame.EMPTY);
                    }
                }
                bool superkoBanned=false;
                if(ordinaryLegal&&positionalSuperko&&loc!=oldKo)
                {
                    int candidateStoneCount=oldStoneCount+1-captured-selfRemoved;
                    superkoBanned=HistoryMayContainStoneCount(candidateStoneCount);
                    if(superkoBanned)
                    {
                        ComputeHash();
                        superkoBanned=HashSeen(currentHash0,currentHash1,currentHash2,currentHash3);
                    }
                }
                if(ordinaryLegal&&!superkoBanned)mask|=1;
                if(superkoBanned)mask|=2;
                RestoreProbeBoard(oldStoneCount,oldHash0,oldHash1,oldHash2,oldHash3);
                moveMaskProbe=false;
            }
            moveMaskCache[loc]=mask;moveMaskComputed[loc]=true;
            return mask;
        }

        public float TerminalValueForSideToMove()
        {
            if(gameState==GoGame.STATE_DRAW)return 0f;
            if(gameState==GoGame.STATE_BLACK_WINS)return sideToMove==GoGame.BLACK?1f:-1f;
            if(gameState==GoGame.STATE_WHITE_WINS)return sideToMove==GoGame.WHITE?1f:-1f;
            if(gameState==GoGame.STATE_RESIGNED)return winner==sideToMove?1f:-1f;
            return 0f;
        }

        private bool CanAttempt(int loc){return gameState==GoGame.STATE_PLAYING&&moveCount<GoGame.MAX_MOVES&&positionHistoryCount<GoGame.MAX_POSITION_HISTORY&&loc>=0&&loc<GoGame.AREA&&board[loc]==GoGame.EMPTY&&loc!=koLoc;}
        private bool IsAtCapacity(){return moveCount>=GoGame.MAX_MOVES||positionHistoryCount>=GoGame.MAX_POSITION_HISTORY;}

        private int CaptureDead(int start,int color,ref int single)
        {
            if(board[start]!=color)return 0; int size=CollectGroup(start,color); if(CountLiberties()!=0)return 0;
            single=size==1?group[0]:GoGame.NONE; for(int i=0;i<size;i++)SetBoardValue(group[i],GoGame.EMPTY); return size;
        }

        private int CollectGroup(int start,int color)
        {
            groupStamp++;if(groupStamp==0){for(int i=0;i<GoGame.AREA;i++){visitedStamp[i]=0;libertyStamp[i]=0;}groupStamp=1;}
            currentLibertyCount=0;int head=0; int tail=0; int size=0; queue[tail++]=start; visitedStamp[start]=groupStamp;
            while(head<tail){int loc=queue[head++];group[size++]=loc;PushNeighbor(Left(loc),color,ref tail);PushNeighbor(Right(loc),color,ref tail);PushNeighbor(Up(loc),color,ref tail);PushNeighbor(Down(loc),color,ref tail);} return size;
        }

        private void PushNeighbor(int loc,int color,ref int tail)
        {if(loc<0)return;int value=board[loc];if(value==GoGame.EMPTY){if(libertyStamp[loc]!=groupStamp){libertyStamp[loc]=groupStamp;currentLibertyCount++;}}else if(value==color&&visitedStamp[loc]!=groupStamp){visitedStamp[loc]=groupStamp;queue[tail++]=loc;}}

        private int CountLiberties(){return currentLibertyCount;}

        private void FinishByScore()
        {
            float score=areaScoring?ComputeAreaScore():ComputeTerritoryScore();
            if(score>0f){winner=GoGame.WHITE;gameState=GoGame.STATE_WHITE_WINS;}else if(score<0f){winner=GoGame.BLACK;gameState=GoGame.STATE_BLACK_WINS;}else{winner=GoGame.EMPTY;gameState=GoGame.STATE_DRAW;}
        }

        private void FinishByMoveLimit()
        {
            FinishByScore();
        }

        private float ComputeAreaScore()
        {
            int black=0; int white=0; for(int i=0;i<GoGame.AREA;i++){if(board[i]==GoGame.BLACK)black++;else if(board[i]==GoGame.WHITE)white++;regionVisited[i]=false;}
            for(int start=0;start<GoGame.AREA;start++)
            {
                if(board[start]!=GoGame.EMPTY||regionVisited[start])continue;
                int head=0;int tail=0;int count=0;bool touchesBlack=false;bool touchesWhite=false;queue[tail++]=start;regionVisited[start]=true;
                while(head<tail){int loc=queue[head++];count++;TouchRegion(Left(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchRegion(Right(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchRegion(Up(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchRegion(Down(loc),ref tail,ref touchesBlack,ref touchesWhite);}
                if(touchesBlack&&!touchesWhite)black+=count;else if(touchesWhite&&!touchesBlack)white+=count;
            }
            finalBlackArea=black;finalWhiteArea=white;finalWhiteMinusBlackScore=white+komiTimes2*0.5f-black;return finalWhiteMinusBlackScore;
        }

        private float ComputeTerritoryScore()
        {
            int black=blackCaptures;int white=whiteCaptures;for(int i=0;i<GoGame.AREA;i++)regionVisited[i]=false;
            for(int start=0;start<GoGame.AREA;start++)
            {
                if(board[start]!=GoGame.EMPTY||regionVisited[start])continue;
                int head=0;int tail=0;int count=0;bool touchesBlack=false;bool touchesWhite=false;queue[tail++]=start;regionVisited[start]=true;
                while(head<tail){int loc=queue[head++];count++;TouchRegion(Left(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchRegion(Right(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchRegion(Up(loc),ref tail,ref touchesBlack,ref touchesWhite);TouchRegion(Down(loc),ref tail,ref touchesBlack,ref touchesWhite);}
                if(touchesBlack&&!touchesWhite)black+=count;else if(touchesWhite&&!touchesBlack)white+=count;
            }
            finalBlackArea=black;finalWhiteArea=white;finalWhiteMinusBlackScore=white+komiTimes2*0.5f-black;return finalWhiteMinusBlackScore;
        }

        private void TouchRegion(int loc,ref int tail,ref bool touchesBlack,ref bool touchesWhite)
        {if(loc<0)return;int value=board[loc];if(value==GoGame.BLACK)touchesBlack=true;else if(value==GoGame.WHITE)touchesWhite=true;else if(!regionVisited[loc]){regionVisited[loc]=true;queue[tail++]=loc;}}

        private void BeginProbe()
        {
            probeId++;if(probeId==0){for(int i=0;i<GoGame.AREA;i++)probeMark[i]=0;probeId=1;}
            probeChangedCount=0;probeActive=true;
        }

        private void EnsureMoveMaskCache()
        {
            if(moveMaskCacheValid)return;
            for(int i=0;i<GoGame.AREA;i++)moveMaskComputed[i]=false;
            moveMaskCacheValid=true;
        }

        private void SetProbeValue(int loc,int value)
        {
            if(loc<0||loc>=GoGame.AREA||board[loc]==value)return;
            if(probeActive&&probeMark[loc]!=probeId)
            {
                probeMark[loc]=probeId;probeChangedLoc[probeChangedCount]=loc;probeOldValue[probeChangedCount]=board[loc];probeChangedCount++;
            }
            board[loc]=value;
        }

        private void SetBoardValue(int loc,int value)
        {
            if(probeActive)SetProbeValue(loc,value);else board[loc]=value;
        }

        private void RestoreProbeBoard(int oldStoneCount,int oldHash0,int oldHash1,int oldHash2,int oldHash3)
        {
            for(int i=probeChangedCount-1;i>=0;i--)board[probeChangedLoc[i]]=probeOldValue[i];
            probeChangedCount=0;probeActive=false;currentStoneCount=oldStoneCount;
            currentHash0=oldHash0;currentHash1=oldHash1;currentHash2=oldHash2;currentHash3=oldHash3;
            if(!moveMaskProbe)InvalidateMoveMaskCache();
        }

        private void RestoreProbe(int oldBlackCaptures,int oldWhiteCaptures,int oldKo,int oldHash0,int oldHash1,int oldHash2,int oldHash3,int oldStoneCount)
        {Copy(backup,board);blackCaptures=oldBlackCaptures;whiteCaptures=oldWhiteCaptures;koLoc=oldKo;currentHash0=oldHash0;currentHash1=oldHash1;currentHash2=oldHash2;currentHash3=oldHash3;currentStoneCount=oldStoneCount;InvalidateMoveMaskCache();}

        private void Reject(int oldBlackCaptures,int oldWhiteCaptures,int oldKo)
        {Copy(backup,board);blackCaptures=oldBlackCaptures;whiteCaptures=oldWhiteCaptures;koLoc=oldKo;ComputeHash();InvalidateMoveMaskCache();}

        private void PushRecentMove(int pla,int loc){for(int i=4;i>0;i--){recentMoveLoc[i]=recentMoveLoc[i-1];recentMovePla[i]=recentMovePla[i-1];}recentMoveLoc[0]=loc;recentMovePla[0]=pla;}
        private void AppendHash()
        {
            if(positionHistoryCount>=GoGame.MAX_POSITION_HISTORY)return;
            if(historyStoneCountsTrusted)
            {
                EnsureHistoryStoneCountFrequency();
                if(historyStoneCountFrequencyValid&&currentStoneCount>=0&&currentStoneCount<=GoGame.AREA)
                    historyStoneCountFrequency[currentStoneCount]++;
            }
            hash0[positionHistoryCount]=currentHash0;hash1[positionHistoryCount]=currentHash1;
            hash2[positionHistoryCount]=currentHash2;hash3[positionHistoryCount]=currentHash3;
            historyStoneCount[positionHistoryCount]=currentStoneCount;
            if(historyStoneCountFrequencyValid&&currentStoneCount>=0&&currentStoneCount<=GoGame.AREA)
            {
                historyBucketNext[positionHistoryCount]=historyBucketHead[currentStoneCount];
                historyBucketHead[currentStoneCount]=positionHistoryCount+1;
            }
            positionHistoryCount++;
        }
        private bool HashSeen(int a,int b,int c,int d)
        {
            historyQueryCount++;
            if(historyStoneCountsTrusted&&historyStoneCountFrequencyValid&&
                (currentStoneCount<0||currentStoneCount>GoGame.AREA||
                 historyStoneCountFrequency[currentStoneCount]==0))
            {historyBucketMissCount++;return false;}
            if(historyStoneCountsTrusted&&historyStoneCountFrequencyValid&&
                currentStoneCount>=0&&currentStoneCount<=GoGame.AREA)
            {
                historyBucketHitCount++;
                int examinedBefore=historyEntriesExamined;
                int head=historyBucketHead[currentStoneCount];
                while(head!=0)
                {
                    int i=head-1;
                    historyEntriesExamined++;
                    if(hash0[i]==a&&hash1[i]==b&&hash2[i]==c&&hash3[i]==d)return true;
                    head=historyBucketNext[i];
                }
                int examined=historyEntriesExamined-examinedBefore;
                if(examined>historyMaxEntriesExamined)historyMaxEntriesExamined=examined;
                return false;
            }
            historyFullScanCount++;
            int fullExaminedBefore=historyEntriesExamined;
            for(int i=0;i<positionHistoryCount;i++)
            {
                historyEntriesExamined++;
                if(hash0[i]==a&&hash1[i]==b&&hash2[i]==c&&hash3[i]==d)return true;
            }
            int fullExamined=historyEntriesExamined-fullExaminedBefore;
            if(fullExamined>historyMaxEntriesExamined)historyMaxEntriesExamined=fullExamined;
            return false;
        }
        private bool HistoryMayContainStoneCount(int count)
        {
            if(!historyStoneCountsTrusted)return true;
            EnsureHistoryStoneCountFrequency();
            if(!historyStoneCountFrequencyValid)return true;
            return count>=0&&count<=GoGame.AREA&&historyStoneCountFrequency[count]>0;
        }

        private void EnsureHistoryStoneCountFrequency()
        {
            if(historyStoneCountFrequency==null||historyStoneCountFrequency.Length<GoGame.AREA+1)
            {
                historyStoneCountFrequency=new int[GoGame.AREA+1];
                historyStoneCountFrequencyValid=false;
            }
            if(historyStoneCountFrequencyValid)return;
            for(int i=0;i<=GoGame.AREA;i++)historyStoneCountFrequency[i]=0;
            for(int i=0;i<=GoGame.AREA;i++)historyBucketHead[i]=0;
            if(!historyStoneCountsTrusted||historyStoneCount==null)
            {historyStoneCountFrequencyValid=false;return;}
            int count=Mathf.Clamp(positionHistoryCount,0,GoGame.MAX_POSITION_HISTORY);
            bool complete=true;
            for(int i=0;i<count;i++)
            {
                int stones=historyStoneCount[i];
                if(stones<0||stones>GoGame.AREA){complete=false;continue;}
                historyStoneCountFrequency[stones]++;
            }
            historyStoneCountFrequencyValid=complete;
            if(complete)
            {
                for(int i=0;i<count;i++)
                {
                    int stones=historyStoneCount[i];
                    historyBucketNext[i]=historyBucketHead[stones];
                    historyBucketHead[stones]=i+1;
                }
            }
            historyStoneCountIndexRebuilds++;
        }

        private void RebuildHistoryBuckets(int count)
        {
            if(historyBucketHead==null||historyBucketNext==null)return;
            for(int i=0;i<=GoGame.AREA;i++)historyBucketHead[i]=0;
            for(int i=0;i<=GoGame.AREA;i++)historyStoneCountFrequency[i]=0;
            count=Mathf.Clamp(count,0,GoGame.MAX_POSITION_HISTORY);
            bool complete=true;
            for(int i=0;i<count;i++)
            {
                int stones=historyStoneCount[i];
                if(stones<0||stones>GoGame.AREA)
                {
                    historyStoneCountFrequencyValid=false;
                    return;
                }
                historyStoneCountFrequency[stones]++;
                historyBucketNext[i]=historyBucketHead[stones];
                historyBucketHead[stones]=i+1;
            }
            historyStoneCountFrequencyValid=complete;
        }
        private void ComputeHash()
        {
            int a=-2128831035,b=-1640531527,c=-2048144789,d=-1028477387;int stoneCount=0;for(int i=0;i<GoGame.AREA;i++){int value=board[i]+2;if(board[i]!=GoGame.EMPTY)stoneCount++;a=(a^(value*257+i))*16777619;b=(b+value*65537+i*17)*0x27D4EB2D;c=(c^(value*131071+i*31))*0x165667B1;d=(d+(value*8191^i*127))*0x1B873593;}currentHash0=a;currentHash1=b;currentHash2=c;currentHash3=d;currentStoneCount=stoneCount;
        }

        private void Copy(int[] source,int[] target){for(int i=0;i<GoGame.AREA;i++)target[i]=source[i];}
        private int Left(int loc){return loc%GoGame.SIZE==0?-1:loc-1;}
        private int Right(int loc){return loc%GoGame.SIZE==GoGame.SIZE-1?-1:loc+1;}
        private int Up(int loc){return loc<GoGame.SIZE?-1:loc-GoGame.SIZE;}
        private int Down(int loc){return loc>=GoGame.AREA-GoGame.SIZE?-1:loc+GoGame.SIZE;}
    }
}
