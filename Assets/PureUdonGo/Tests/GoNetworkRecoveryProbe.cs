using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Udon-side reconnect/leave fixture. The editor verifier only starts the
    /// fixture and removes a spawned remote ClientSim player; all match mutations and
    /// recovery observations are performed by this serialized Udon program.
    /// </summary>
    public sealed class GoNetworkRecoveryProbe : UdonSharpBehaviour
    {
        public GoGame undoGame;
        public GoGame drawGame;
        public GoGame aiGame;
        public int departingPlayerId=-1;
        public string departingPlayerName="";

        public bool prepared;
        public bool probeFinished;
        public bool probePassed;
        public string failure="";

        public int localPlayerIdBefore=-1;
        public int undoRecoveryBefore;
        public int drawRecoveryBefore;
        public int aiRecoveryBefore;
        public int aiAuthorityBefore=-1;
        public int undoSeatAfter=-1;
        public int drawSeatAfter=-1;
        public int aiAuthorityAfter=-1;
        public int undoRecoveryAfter;
        public int drawRecoveryAfter;
        public int aiRecoveryAfter;
        public bool undoOfferCleared;
        public bool drawOfferCleared;
        public bool recoveryAdvanced;
        public bool positionsPreserved;
        public bool departingLeaveEventSeen;
        public int leftPlayerId=-1;

        private bool waitingForDisconnect;
        private int disconnectFrames;

        public void RunNetworkRecoveryProbe()
        {
            prepared=false;
            probeFinished=false;
            probePassed=false;
            failure="";
            waitingForDisconnect=false;
            disconnectFrames=0;
            departingLeaveEventSeen=false;
            leftPlayerId=-1;

            if(undoGame==null||drawGame==null||aiGame==null)
            {
                Finish("network recovery probe references are incomplete");
                return;
            }
            if(departingPlayerId<0||departingPlayerName==null||departingPlayerName.Length==0)
            {
                Finish("network recovery probe did not receive a departing remote identity");
                return;
            }
            VRCPlayerApi local=Networking.LocalPlayer;
            if(!Utilities.IsValid(local))
            {
                Finish("ClientSim local player is unavailable before fixture setup");
                return;
            }
            localPlayerIdBefore=local.playerId;

            if(!PrepareUndoFixture())return;
            if(!PrepareDrawFixture())return;
            if(!PrepareAiAuthorityFixture())return;

            undoRecoveryBefore=undoGame.networkRecoveryRevision;
            drawRecoveryBefore=drawGame.networkRecoveryRevision;
            aiRecoveryBefore=aiGame.networkRecoveryRevision;
            if(aiAuthorityBefore!=localPlayerIdBefore)
            {
                Finish("AI-AI control authority was not assigned to the local controller");
                return;
            }
            prepared=true;
            waitingForDisconnect=true;
        }

        public void Update()
        {
            if(!waitingForDisconnect||probeFinished)return;
            if(!departingLeaveEventSeen)return;
            disconnectFrames++;
            if(disconnectFrames<4)return;

            waitingForDisconnect=false;
            undoSeatAfter=undoGame.blackPlayerId;
            drawSeatAfter=drawGame.blackPlayerId;
            aiAuthorityAfter=aiGame.aiControlAuthorityPlayerId;
            undoRecoveryAfter=undoGame.networkRecoveryRevision;
            drawRecoveryAfter=drawGame.networkRecoveryRevision;
            aiRecoveryAfter=aiGame.networkRecoveryRevision;

            bool undoSeatCleared=undoSeatAfter<0;
            bool drawSeatCleared=drawSeatAfter<0;
            undoOfferCleared=undoGame.undoOfferSide==GoGame.EMPTY&&
                undoGame.undoOfferMoveCount==-1;
            drawOfferCleared=drawGame.drawOfferSide==GoGame.EMPTY&&
                drawGame.drawOfferMoveCount==-1;
            bool authorityCleared=aiAuthorityAfter<0&&
                (aiGame.aiControlAuthorityName==null||aiGame.aiControlAuthorityName.Length==0);
            recoveryAdvanced=undoRecoveryAfter>undoRecoveryBefore&&
                drawRecoveryAfter>drawRecoveryBefore&&aiRecoveryAfter>aiRecoveryBefore;
            positionsPreserved=undoGame.moveCount==1&&drawGame.moveCount==1&&
                undoGame.gameState==GoGame.STATE_PLAYING&&drawGame.gameState==GoGame.STATE_PLAYING;
            probePassed=undoSeatCleared&&drawSeatCleared&&undoOfferCleared&&
                drawOfferCleared&&authorityCleared&&recoveryAdvanced&&positionsPreserved;
            if(!probePassed)
            {
                Finish("leave recovery invariant failed");
                return;
            }
            probeFinished=true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if(player!=null&&player.playerId==departingPlayerId)
            {
                departingLeaveEventSeen=true;
                leftPlayerId=player.playerId;
            }
        }

        private bool PrepareUndoFixture()
        {
            undoGame.SetMatchMode(GoAiController.MODE_PVP);
            undoGame.ClaimBlack();
            undoGame.RequestStartMatch();
            if(!undoGame.TryPlay(0))
            {
                Finish("PvP undo fixture could not commit its opening move");
                return false;
            }
            // A single ClientSim VM cannot execute the opposing player's
            // turn. Model the authoritative turn hand-off explicitly so the
            // local Black seat is again the current Go player before it sends
            // the real consent request. This keeps the production
            // current-player permission check enabled instead of bypassing
            // RequestUndo or fabricating the offer fields.
            undoGame.sideToMove=GoGame.BLACK;
            undoGame.revision++;
            undoGame.RequestUndo();
            if(undoGame.undoOfferSide!=GoGame.BLACK||undoGame.undoOfferMoveCount!=1)
            {
                Finish("PvP undo fixture did not publish a current undo offer");
                return false;
            }
            undoGame.blackPlayerId=departingPlayerId;
            undoGame.blackPlayerName=departingPlayerName;
            undoGame.undoOfferRevision=undoGame.revision;
            return true;
        }

        private bool PrepareDrawFixture()
        {
            drawGame.SetMatchMode(GoAiController.MODE_PVP);
            drawGame.ClaimBlack();
            drawGame.RequestStartMatch();
            if(!drawGame.TryPlay(1))
            {
                Finish("PvP draw fixture could not commit its opening move");
                return false;
            }
            drawGame.OfferOrAcceptDraw();
            if(drawGame.drawOfferSide!=GoGame.BLACK||drawGame.drawOfferMoveCount!=1)
            {
                Finish("PvP draw fixture did not publish a current draw offer");
                return false;
            }
            drawGame.blackPlayerId=departingPlayerId;
            drawGame.blackPlayerName=departingPlayerName;
            drawGame.drawOfferRevision=drawGame.revision;
            return true;
        }

        private bool PrepareAiAuthorityFixture()
        {
            aiGame.SetMatchMode(GoAiController.MODE_AIVAI);
            aiAuthorityBefore=aiGame.aiControlAuthorityPlayerId;
            if(aiAuthorityBefore<0)
            {
                Finish("AI-AI mode did not assign a local controller authority");
                return false;
            }
            aiGame.aiControlAuthorityPlayerId=departingPlayerId;
            aiGame.aiControlAuthorityName=departingPlayerName;
            return true;
        }

        private void Finish(string message)
        {
            failure=message==null?"unknown network recovery probe failure":message;
            waitingForDisconnect=false;
            probeFinished=true;
            probePassed=false;
        }
    }
}
