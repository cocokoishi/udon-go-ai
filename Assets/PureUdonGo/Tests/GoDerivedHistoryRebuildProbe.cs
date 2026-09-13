using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Replays a long authoritative move log into a fresh table-local cache,
    /// then compares every derived hash/stone-count entry, legal/superko mask,
    /// and complete KataGo feature tensor. This is deterministic rebuild
    /// evidence; real cross-client transport remains a separate manual gate.
    /// </summary>
    public sealed class GoDerivedHistoryRebuildProbe : UdonSharpBehaviour
    {
        private const int BUILD=1,COPY_AND_REBUILD=2,COMPARE_HISTORY=3,
            WARM_MOVE_MASK=4,COMPARE_MASKS=5,ENCODE_SOURCE=6,
            ENCODE_REPLICA=7,COMPARE_FEATURES=8;
        public GoGame source;
        public GoGame replica;
        public GoAiController sourceController;
        public GoAiController replicaController;
        public GoFeatureEncoder sourceEncoder;
        public GoFeatureEncoder replicaEncoder;
        public GoBoardPool boardPool;
        public int[] moves=
        {
            60,300,72,288,40,320,54,59,61,299,91,269,43,317,111,235,148,119,
            354,7,79,173,181,197,223,105,90,208,175,2,115,65,304,164,36,254,
            152,53,129,338,117,76,312,201,167,240,191,337,135,67,-1,74,262,
            276,114,315,17,44,37,270,333,16,274,62,3,318,243,204,136,131,190,
            149,237,143,87,134,196,71,293,278,125,69,47,242,29,42,228,279,108,
            33,216,218,6,339,-1,277,324,257,230,24,100,153,336,210,101,249,170,
            305,310,80,99,326,217,144,81,195,241,22,261,158,49,1,275,226,244,
            86,316,325,107,301,247,98,0,329,172,19,342,214,-1,185,307,189,97,
            13,349,48,106,4,199,84,347,166,295,133,219,130,255,171,357,121
        };
        public bool probeFinished;
        public bool probePassed;
        public int phase;
        public int moveIndex;
        public int compareCursor;
        public int hashMismatches;
        public int stoneCountMismatches;
        public int legalMaskMismatches;
        public int superkoMaskMismatches;
        public int featureMismatches;
        public int moveMaskWarmupFrames;
        public int moveMaskWarmupPoints;
        public int moveMaskWarmupMaxPointsOneFrame;
        public bool rebuildSucceeded;
        public bool transactionalCorruptionPassed;
        public int estimatedGoGameIntArrayBytes;
        public int legacyRawSyncedBytes;
        public string failure="";

        public void RunDerivedHistoryRebuildProbe()
        {
            probeFinished=false;probePassed=false;phase=0;moveIndex=0;compareCursor=0;
            hashMismatches=0;stoneCountMismatches=0;legalMaskMismatches=0;
            superkoMaskMismatches=0;featureMismatches=0;rebuildSucceeded=false;
            moveMaskWarmupFrames=0;moveMaskWarmupPoints=0;
            moveMaskWarmupMaxPointsOneFrame=0;
            transactionalCorruptionPassed=false;
            estimatedGoGameIntArrayBytes=GoGame.EstimatedGoGameIntArrayBytes;
            legacyRawSyncedBytes=GoGame.LegacyRawSyncedBytes;failure="";
            if(source==null||replica==null||source==replica||sourceController==null||
                replicaController==null||sourceEncoder==null||replicaEncoder==null||
                moves==null||moves.Length!=160)
            {Finish("derived-history probe references are incomplete");return;}
            sourceController.autoStart=false;replicaController.autoStart=false;
            sourceController.InvalidateSearch();replicaController.InvalidateSearch();
            // Keep background warm-up out of the deterministic rebuild trace
            // from its first frame. The pool/hover path has its own gate; this
            // probe owns both tables for exact source-vs-replica accounting.
            if(boardPool!=null)boardPool.enabled=false;
            if(source.view!=null)source.view.enabled=false;
            if(replica.view!=null)replica.view.enabled=false;
            source.SetMatchMode(GoAiController.MODE_AIVAI);
            source.RequestForceReset();source.SetBlackStarts();source.RequestStartMatch();
            replica.SetMatchMode(GoAiController.MODE_AIVAI);replica.RequestForceReset();
            phase=BUILD;
        }

        public void Update()
        {
            if(probeFinished||phase==0)return;
            if(phase==BUILD)
            {
                if(moveIndex>=moves.Length){phase=COPY_AND_REBUILD;return;}
                int move=moves[moveIndex];int before=source.moveCount;bool accepted;
                if(move==GoGame.PASS)
                {source.PassFromAI();accepted=source.moveCount==before+1;}
                else accepted=source.TryPlayFromAI(move);
                if(!accepted){Finish("source history rejected move index="+moveIndex);return;}
                moveIndex++;return;
            }
            if(phase==COPY_AND_REBUILD)
            {
                CopySynchronizedSnapshot();
                rebuildSucceeded=replica.RebuildDerivedHistory();
                if(!rebuildSucceeded){Finish("fresh replica rejected synchronized move log");return;}
                compareCursor=0;phase=COMPARE_HISTORY;return;
            }
            if(phase==COMPARE_HISTORY)
            {
                int end=compareCursor+32;
                if(end>source.positionHistoryCount)end=source.positionHistoryCount;
                while(compareCursor<end)
                {
                    int i=compareCursor;
                    if(source.hash0[i]!=replica.hash0[i]||
                        source.hash1[i]!=replica.hash1[i]||
                        source.hash2[i]!=replica.hash2[i]||
                        source.hash3[i]!=replica.hash3[i])hashMismatches++;
                    if(source.historyStoneCount[i]!=replica.historyStoneCount[i])
                        stoneCountMismatches++;
                    compareCursor++;
                }
                if(compareCursor<source.positionHistoryCount)return;
                compareCursor=0;phase=WARM_MOVE_MASK;return;
            }
            if(phase==WARM_MOVE_MASK)
            {
                // The generated room pool may have warmed part of the source
                // cache while the 160-ply fixture was being replayed. Reset
                // both test positions at the measurement boundary so the
                // gate counts a complete 361-point warm-up, not only the
                // points that happened to remain after background work.
                if(moveMaskWarmupFrames==0)
                {
                    // Isolate this exact parity measurement from the room
                    // scheduler. The dedicated BoardPool gate covers the
                    // pooled path; here both positions must advance through
                    // the same explicit 24-point loop without a concurrent
                    // background batch stealing points from the delta.
                    if(source.view!=null)
                    {
                        source.view.moveMaskWarmupManagedByPool=false;
                        source.view.enabled=false;
                    }
                    if(replica.view!=null)
                    {
                        replica.view.moveMaskWarmupManagedByPool=false;
                        replica.view.enabled=false;
                    }
                    if(boardPool!=null)boardPool.enabled=false;
                    source.ResetMoveMaskCacheForVerifier();
                    replica.ResetMoveMaskCacheForVerifier();
                }
                int computed=source.StepMoveMaskCacheTimeSliced(
                    GoGame.MOVE_MASK_WARMUP_DEFAULT_MS,24);
                replica.StepMoveMaskCacheTimeSliced(
                    GoGame.MOVE_MASK_WARMUP_DEFAULT_MS,24);
                moveMaskWarmupPoints+=computed;
                moveMaskWarmupFrames++;
                if(computed>moveMaskWarmupMaxPointsOneFrame)
                    moveMaskWarmupMaxPointsOneFrame=computed;
                if(!source.IsMoveMaskCacheComplete()||
                    !replica.IsMoveMaskCacheComplete())return;
                compareCursor=0;phase=COMPARE_MASKS;return;
            }
            if(phase==COMPARE_MASKS)
            {
                int end=compareCursor+8;if(end>GoGame.AREA)end=GoGame.AREA;
                while(compareCursor<end)
                {
                    int sourceMask=source.GetMoveMask(compareCursor);
                    int replicaMask=replica.GetMoveMask(compareCursor);
                    if((sourceMask&1)!=(replicaMask&1))legalMaskMismatches++;
                    if((sourceMask&2)!=(replicaMask&2))superkoMaskMismatches++;
                    compareCursor++;
                }
                if(compareCursor<GoGame.AREA)return;
                if(!sourceEncoder.BeginEncodeGame(source))
                {Finish("source feature encoder did not start");return;}
                phase=ENCODE_SOURCE;return;
            }
            if(phase==ENCODE_SOURCE)
            {
                int state=sourceEncoder.StepEncode(64);
                if(state==GoFeatureEncoder.ENCODE_RUNNING)return;
                if(state!=GoFeatureEncoder.ENCODE_COMPLETE)
                {Finish("source feature encoding failed: "+sourceEncoder.lastError);return;}
                if(!replicaEncoder.BeginEncodeGame(replica))
                {Finish("replica feature encoder did not start");return;}
                phase=ENCODE_REPLICA;return;
            }
            if(phase==ENCODE_REPLICA)
            {
                int state=replicaEncoder.StepEncode(64);
                if(state==GoFeatureEncoder.ENCODE_RUNNING)return;
                if(state!=GoFeatureEncoder.ENCODE_COMPLETE)
                {Finish("replica feature encoding failed: "+replicaEncoder.lastError);return;}
                compareCursor=0;phase=COMPARE_FEATURES;return;
            }
            int spatialEnd=compareCursor+256;
            if(spatialEnd>GoFeatureEncoder.SPATIAL_COUNT)
                spatialEnd=GoFeatureEncoder.SPATIAL_COUNT;
            while(compareCursor<spatialEnd)
            {
                if(sourceEncoder.spatialOutput[compareCursor]!=
                    replicaEncoder.spatialOutput[compareCursor])featureMismatches++;
                compareCursor++;
            }
            if(compareCursor<GoFeatureEncoder.SPATIAL_COUNT)return;
            for(int i=0;i<GoFeatureEncoder.GLOBAL_CHANNELS;i++)
                if(sourceEncoder.globalOutput[i]!=replicaEncoder.globalOutput[i])
                    featureMismatches++;
            bool exact=hashMismatches==0&&stoneCountMismatches==0&&
                legalMaskMismatches==0&&superkoMaskMismatches==0&&
                featureMismatches==0&&replica.positionHistoryCount==
                source.positionHistoryCount&&estimatedGoGameIntArrayBytes<legacyRawSyncedBytes&&
                moveMaskWarmupPoints==GoGame.AREA&&moveMaskWarmupFrames>1&&
                moveMaskWarmupMaxPointsOneFrame<=24;
            if(!exact)
            {
                Finish("derived rebuild mismatch hash="+hashMismatches+
                    " stones="+stoneCountMismatches+" legal="+legalMaskMismatches+
                    " superko="+superkoMaskMismatches+" features="+featureMismatches+
                    " maskPoints="+moveMaskWarmupPoints+
                    " maskFrames="+moveMaskWarmupFrames+
                    " maskMaxFrame="+moveMaskWarmupMaxPointsOneFrame);
                return;
            }
            // Corrupt one packed entry with a syntactically valid move from
            // the wrong turn.  Replay therefore fails after earlier moves
            // have already mutated the scratch state.  The transaction must
            // restore every authoritative/derived field exactly, including
            // the malformed entry itself.
            transactionalCorruptionPassed=RunTransactionalCorruptionCheck();
            if(!transactionalCorruptionPassed)
            {
                Finish("transactional rebuild corruption test changed authoritative state");
                return;
            }
            probePassed=true;probeFinished=true;phase=0;
        }

        private bool RunTransactionalCorruptionCheck()
        {
            const int corruptIndex=81;
            if(replica==null||source==null||replica.moveCount<=corruptIndex||
                replica.moveHistory==null||replica.moveHistory.Length<=corruptIndex)
                return false;
            int beforePositionHistory=replica.positionHistoryCount;
            int beforeSide=replica.sideToMove;
            int beforeMoveCount=replica.moveCount;
            int beforePasses=replica.consecutivePasses;
            int beforeBlackCaptures=replica.blackCaptures;
            int beforeWhiteCaptures=replica.whiteCaptures;
            int beforeState=replica.gameState;
            int beforeWinner=replica.winner;
            int beforeLastMove=replica.lastMove;
            int beforeKo=replica.koLoc;
            int beforeFinalBlack=replica.finalBlackArea;
            int beforeFinalWhite=replica.finalWhiteArea;
            float beforeFinalScore=replica.finalWhiteMinusBlackScore;
            string beforeAction=replica.lastActionText;
            int beforeHistoryVersion=replica.historyStoneCountVersion;
            int corruptedPacked=replica.moveHistory[0];
            int originalPacked=replica.moveHistory[corruptIndex];
            replica.moveHistory[corruptIndex]=corruptedPacked;
            bool rebuilt=replica.TryRebuildPositionToMoveCount(beforeMoveCount);
            bool unchanged=!rebuilt&&replica.positionHistoryCount==beforePositionHistory&&
                replica.sideToMove==beforeSide&&replica.moveCount==beforeMoveCount&&
                replica.consecutivePasses==beforePasses&&
                replica.blackCaptures==beforeBlackCaptures&&
                replica.whiteCaptures==beforeWhiteCaptures&&
                replica.gameState==beforeState&&replica.winner==beforeWinner&&
                replica.lastMove==beforeLastMove&&replica.koLoc==beforeKo&&
                replica.finalBlackArea==beforeFinalBlack&&
                replica.finalWhiteArea==beforeFinalWhite&&
                Mathf.Abs(replica.finalWhiteMinusBlackScore-beforeFinalScore)<0.00001f&&
                replica.lastActionText==beforeAction&&
                replica.historyStoneCountVersion==beforeHistoryVersion&&
                replica.moveHistory[corruptIndex]==corruptedPacked;
            for(int i=0;i<GoGame.AREA&&unchanged;i++)
                if(replica.board[i]!=source.board[i]||
                    replica.previousBoard1[i]!=source.previousBoard1[i]||
                    replica.previousBoard2[i]!=source.previousBoard2[i])unchanged=false;
            for(int i=0;i<GoGame.MAX_POSITION_HISTORY&&unchanged;i++)
                if(replica.hash0[i]!=source.hash0[i]||replica.hash1[i]!=source.hash1[i]||
                    replica.hash2[i]!=source.hash2[i]||replica.hash3[i]!=source.hash3[i]||
                    replica.historyStoneCount[i]!=source.historyStoneCount[i])unchanged=false;
            for(int i=0;i<GoGame.MAX_MOVES&&unchanged;i++)
                if(i!=corruptIndex&&replica.moveHistory[i]!=source.moveHistory[i])unchanged=false;
            // Keep the intentionally malformed input in place for the caller;
            // a failed transaction must not silently repair authoritative data.
            if(replica.moveHistory[corruptIndex]!=corruptedPacked)
                unchanged=false;
            if(originalPacked==corruptedPacked)unchanged=false;
            return unchanged;
        }

        private void CopySynchronizedSnapshot()
        {
            for(int i=0;i<GoGame.AREA;i++)
            {
                replica.board[i]=source.board[i];
                replica.previousBoard1[i]=source.previousBoard1[i];
                replica.previousBoard2[i]=source.previousBoard2[i];
            }
            for(int i=0;i<5;i++)
            {replica.recentMoveLoc[i]=source.recentMoveLoc[i];replica.recentMovePla[i]=source.recentMovePla[i];}
            for(int i=0;i<GoGame.MAX_MOVES;i++)replica.moveHistory[i]=source.moveHistory[i];
            replica.positionHistoryCount=source.positionHistoryCount;
            replica.sideToMove=source.sideToMove;replica.moveCount=source.moveCount;
            replica.consecutivePasses=source.consecutivePasses;
            replica.blackCaptures=source.blackCaptures;replica.whiteCaptures=source.whiteCaptures;
            replica.gameState=source.gameState;replica.winner=source.winner;
            replica.lastMove=source.lastMove;replica.koLoc=source.koLoc;
            replica.revision=source.revision;replica.settingsRevision=source.settingsRevision;
            replica.handicapStones=source.handicapStones;replica.komiTimes2=source.komiTimes2;
            replica.finalBlackArea=source.finalBlackArea;replica.finalWhiteArea=source.finalWhiteArea;
            replica.finalWhiteMinusBlackScore=source.finalWhiteMinusBlackScore;
            replica.matchStarted=source.matchStarted;replica.blackIsAI=source.blackIsAI;
            replica.whiteIsAI=source.whiteIsAI;replica.starter=source.starter;
        }

        private void Finish(string message)
        {failure=message;phase=0;probeFinished=true;}
    }
}
