using UdonSharp;
using UnityEngine.UI;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim fixture for the multiplayer disruption guard. The editor
    /// harness first creates a real one-move PvP position, reassigns the seat
    /// to a spawned remote player, and transfers the table owner. The local
    /// spectator then attempts every reset/undo endpoint; none may mutate the
    /// authoritative position or remain interactable in the UI.
    /// </summary>
    public sealed class GoPermissionGuardProbe : UdonSharpBehaviour
    {
        public GoGame game;
        public GoUI ui;
        public int remotePlayerId = -1;
        public string remotePlayerName = "";
        public bool prepared;
        public bool reassigned;
        public bool probeFinished;
        public bool resetDenied;
        public bool newGameDenied;
        public bool undoDenied;
        public bool resetButtonDisabled;
        public bool newGameButtonDisabled;
        public bool undoButtonDisabled;
        public int beforeRevision;
        public int afterRevision;
        public int beforeMoveCount;
        public int afterMoveCount;
        public int beforeBoard0;
        public int afterBoard0;
        public string failure = "";

        public void PrepareOwnedPosition()
        {
            prepared = false;
            reassigned = false;
            probeFinished = false;
            failure = "";
            if (game == null)
            {
                failure = "permission guard game reference is missing";
                return;
            }
            // The simulated witness may have claimed a generated seat during
            // OnPlayerJoined. Clear that stale setup while the local player is
            // still the table owner; the following ReassignSeatToRemote call
            // is the lifecycle under test.
            game.blackPlayerId=-1;
            game.blackPlayerName="";
            game.whitePlayerId=-1;
            game.whitePlayerName="";
            game.matchStarted=false;
            game.SetMatchMode(GoAiController.MODE_PVP);
            game.RequestForceReset();
            // ForceReset intentionally invalidates the local derived-history
            // readiness flag. Warm it through the production rebuild entry
            // before exercising the real PvP move/permission path.
            game.RebuildDerivedHistory();
            game.SetBlackStarts();
            game.ClaimBlack();
            game.RequestStartMatch();
            game.RebuildDerivedHistory();
            bool historyReady=game.HasVerifiedPositionHistory();
            if (!game.TryPlay(0))
            {
                failure = "permission guard could not build the PvP fixture move" +
                    " state=" + game.gameState + " started=" + game.matchStarted +
                    " side=" + game.sideToMove + " moveCount=" + game.moveCount +
                    " blackId=" + game.blackPlayerId + " historyReady=" + historyReady +
                    " positionHistoryCount=" + game.positionHistoryCount +
                    " historyVersion=" + game.historyStoneCountVersion +
                    " action=" + game.lastActionText;
                return;
            }
            prepared = game.moveCount == 1 && game.board[0] == GoGame.BLACK &&
                game.matchStarted;
        }

        public void ReassignSeatToRemote()
        {
            if (!prepared || game == null || remotePlayerId < 0)
            {
                failure = "permission guard remote seat inputs are incomplete";
                return;
            }
            // Keep the one-move authoritative position, but make the only
            // occupied seat belong to the remote owner before ownership is
            // transferred by the editor harness.
            game.blackPlayerId = remotePlayerId;
            game.blackPlayerName = remotePlayerName == null ? "" : remotePlayerName;
            game.whitePlayerId = -1;
            game.whitePlayerName = "";
            game.revision++;
            game.lastActionText = "Remote player owns the PvP seat";
            game.RequestSerialization();
            reassigned = true;
        }

        public void RunSpectatorGuard()
        {
            probeFinished = false;
            if (game == null)
            {
                failure = "permission guard game reference is missing";
                probeFinished = true;
                return;
            }
            beforeRevision = game.revision;
            beforeMoveCount = game.moveCount;
            beforeBoard0 = game.board[0];
            bool canReset = game.CanLocalForceReset();
            bool canUndo = game.CanLocalUndo();

            game.RequestNewGame();
            int afterNewGameRevision = game.revision;
            int afterNewGameMoveCount = game.moveCount;
            game.RequestForceReset();
            afterRevision = game.revision;
            afterMoveCount = game.moveCount;
            afterBoard0 = game.board[0];

            if (ui != null)
            {
                ui.RefreshControlState();
                resetButtonDisabled = ReadButtonState(40) == false;
                newGameButtonDisabled = ReadButtonState(0) == false;
                undoButtonDisabled = ReadButtonState(44) == false;
            }

            newGameDenied = !canReset && afterNewGameRevision == beforeRevision &&
                afterNewGameMoveCount == beforeMoveCount;
            resetDenied = !canReset && afterRevision == beforeRevision &&
                afterMoveCount == beforeMoveCount && afterBoard0 == beforeBoard0;
            game.RequestUndo();
            undoDenied = !canUndo && game.revision == beforeRevision &&
                game.moveCount == beforeMoveCount && game.board[0] == beforeBoard0;
            if (!newGameDenied || !resetDenied || !undoDenied)
            {
                failure = "spectator endpoint changed state or remained interactable";
            }
            probeFinished = true;
        }

        private bool ReadButtonState(int action)
        {
            if (ui == null || ui.controlButtons == null ||
                ui.controlButtonActions == null)
                return true;
            int count = ui.controlButtons.Length < ui.controlButtonActions.Length
                ? ui.controlButtons.Length : ui.controlButtonActions.Length;
            for (int i = 0; i < count; i++)
                if (ui.controlButtonActions[i] == action)
                {
                    Button button = ui.controlButtons[i];
                    return button == null || button.interactable;
                }
            return true;
        }
    }
}
