using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only Udon rules probe. The editor verifier adds this to an
    /// unsaved copy of the generated scene; it is never part of the production
    /// world. All assertions are made after calling the real GoGame Udon
    /// methods, with no editor-side board mutation.
    /// </summary>
    public sealed class GoRulesProbe : UdonSharpBehaviour
    {
        public GoGame game;
        public GoAiController controller;
        public GoSearchState searchState;
        public GoBoardView view;
        public bool probeFinished;
        public bool capturePassed;
        public bool doublePassPassed;
        public bool exactAreaScorePassed;
        public bool suicidePassed;
        public bool koPassed;
        public bool simpleKoPassed;
        public bool searchKoPassed;
        public bool positionalSuperkoPassed;
        public bool multiSuicidePrisonerPassed;
        public bool pureRulesQueryPassed;
        public bool capacityPassed;
        public bool handicapPassed;
        public bool resignPassed;
        public bool previewFinalFieldsPassed;
        public bool capturePresentationPassed;
        public bool multiCapturePresentationPassed;
        public bool newGameNoCapturePresentationPassed;
        public bool forceResetNoCapturePresentationPassed;
        public bool deserializeNoCapturePresentationPassed;
        public int captureMoveCount;
        public int doublePassGameState;
        public int doublePassBlackArea;
        public int doublePassWhiteArea;
        public float doublePassScore;
        public int suicideMoveCount;
        public int koLocation;
        public int koMoveCount;
        public int whitePrisonersAfterSuicide;
        public int handicapCount;
        public int resignState;
        public string failure;
        public int captureProbeStartCount;
        public int captureProbeLastStoneCount;
        public int captureProbeLastRefreshReason;
        public bool captureProbeActive;

        public int[] positionalSuperkoSetupMoves =
        {
            19, -1, 21, -1, 1, 20, -1, 38, -1, 40, -1, 58,
            220, 201, 238, 219, 240, 221, 258, -1,
            196, 215, 214, 233, 216, 235, -1, 253
        };
        public int[] positionalSuperkoCycleMoves = { 39, 239, 234, 20, 220 };

        private int phase;
        private bool running;
        private int positionalSuperkoSetupIndex;
        private int positionalSuperkoCycleIndex;
        private bool positionalSuperkoSetupOk;
        private bool positionalSuperkoCycleOk;
        private bool positionalSuperkoSetupSwitched;
        private bool positionalSuperkoCandidateTested;
        private bool positionalSuperkoFallbackTested;
        private bool positionalSuperkoCandidateRejected;
        private bool positionalSuperkoFallbackAccepted;
        private bool capturePresentationCheckPending;
        private int captureAnimationStartBefore;

        public void RunRulesProbe()
        {
            probeFinished = false;
            capturePassed = false;
            doublePassPassed = false;
            exactAreaScorePassed = false;
            suicidePassed = false;
            koPassed = false;
            simpleKoPassed = false;
            searchKoPassed = false;
            positionalSuperkoPassed = false;
            multiSuicidePrisonerPassed = false;
            pureRulesQueryPassed = false;
            capacityPassed = false;
            handicapPassed = false;
            resignPassed = false;
            previewFinalFieldsPassed = false;
            capturePresentationPassed = false;
            multiCapturePresentationPassed=false;
            newGameNoCapturePresentationPassed=false;
            forceResetNoCapturePresentationPassed=false;
            deserializeNoCapturePresentationPassed=false;
            capturePresentationCheckPending = false;
            failure = "";
            if (game == null || controller == null || searchState == null)
            {
                failure = "probe references are incomplete";
                probeFinished = true;
                return;
            }

            controller.autoStart = false;
            game.SetMatchMode(3);
            phase = 0;
            running = true;
        }

        public void Update()
        {
            if (!running || probeFinished || game == null)
                return;

            if (capturePresentationCheckPending)
            {
                // The animation may finish before this probe's next Update
                // on a slow/paused ClientSim. Verify the stable start counter
                // and refresh reason instead of a transient active flag.
                capturePresentationPassed = capturePresentationPassed ||
                    (view != null && game.board[0] == GoGame.EMPTY &&
                    view.captureAnimationStartCount>captureAnimationStartBefore&&
                    view.lastCaptureAnimationStoneCount==1&&
                    view.lastRefreshReason==GoBoardView.REFRESH_CAPTURE);
                if(view!=null)
                {
                    captureProbeStartCount=view.captureAnimationStartCount;
                    captureProbeLastStoneCount=view.lastCaptureAnimationStoneCount;
                    captureProbeLastRefreshReason=view.lastRefreshReason;
                    captureProbeActive=view.IsCaptureAnimating(0);
                }
                capturePresentationCheckPending = false;
            }

            // Keep each scenario in its own Udon event. The VM has a finite
            // per-event execution budget; serializing every case after one
            // RunRulesProbe event can exceed it even when each operation is
            // finite on its own.
            if (phase == 0) { RunCaptureProbe(); phase++; return; }
            if (phase == 1) { RunDoublePassProbe(); phase++; return; }
            if (phase == 2) { RunSuicideProbe(); phase++; return; }
            if (phase == 3) { RunKoProbe(); phase++; return; }
            if (phase == 4) { RunMultiSuicidePrisonerProbe(); phase++; return; }
            if (phase == 5) { RunPureRulesApiProbe(); phase++; return; }
            if (phase == 6) { RunCapacityProbe(); phase++; return; }
            if (phase == 7) { RunHandicapProbe(); phase++; return; }
            if (phase == 8) { RunResignProbe(); phase++; return; }
            if (phase == 9) { BeginPositionalSuperkoProbe(); phase++; return; }
            if (phase == 10) { RunPositionalSuperkoStep(); return; }
            if (phase == 11) { RunCaptureRefreshReasonProbe(); phase++; return; }

            if (!capturePassed) failure += " capture";
            if (!capturePresentationPassed) failure += " capture-presentation";
            if(!multiCapturePresentationPassed)failure+=" multi-capture-presentation";
            if(!newGameNoCapturePresentationPassed)failure+=" new-game-capture";
            if(!forceResetNoCapturePresentationPassed)failure+=" force-reset-capture";
            if(!deserializeNoCapturePresentationPassed)failure+=" deserialize-capture";
            if (!doublePassPassed) failure += " double-pass";
            if (!exactAreaScorePassed) failure += " exact-area-score";
            if (!suicidePassed) failure += " suicide";
            if (!koPassed) failure += " ko";
            if (!simpleKoPassed) failure += " simple-ko";
            if (!searchKoPassed) failure += " search-ko";
            if (!positionalSuperkoPassed) failure += " positional-superko";
            if (!multiSuicidePrisonerPassed) failure += " multi-suicide-prisoner";
            if (!pureRulesQueryPassed) failure += " pure-rules-api";
            if (!capacityPassed) failure += " capacity";
            if (!handicapPassed) failure += " handicap";
            if (!resignPassed) failure += " resign";
            if (!previewFinalFieldsPassed) failure += " preview-final-fields";
            probeFinished = true;
            running = false;
        }

        private void PrepareEmptyAIMatch()
        {
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
        }

        private void RunCaptureProbe()
        {
            PrepareEmptyAIMatch();
            captureAnimationStartBefore=view==null?0:view.captureAnimationStartCount;
            bool blackOne = game.TryPlayFromAI(1);
            bool whiteOne = game.TryPlayFromAI(0);
            bool blackCapture = game.TryPlayFromAI(19);
            captureMoveCount = game.moveCount;
            capturePassed = blackOne && whiteOne && blackCapture &&
                game.board[0] == GoGame.EMPTY && game.blackCaptures == 1 &&
                game.moveCount == 3 && game.sideToMove == GoGame.WHITE;
            // Capture presentation is emitted synchronously with the
            // authoritative move. Sample it before delayed network-recovery
            // refreshes can legitimately replace lastRefreshReason.
            if(view!=null)
            {
                captureProbeStartCount=view.captureAnimationStartCount;
                captureProbeLastStoneCount=view.lastCaptureAnimationStoneCount;
                captureProbeLastRefreshReason=view.lastRefreshReason;
                captureProbeActive=view.IsCaptureAnimating(0);
                capturePresentationPassed=capturePassed&&
                    captureProbeStartCount>captureAnimationStartBefore&&
                    captureProbeLastStoneCount==1&&
                    captureProbeLastRefreshReason==GoBoardView.REFRESH_CAPTURE;
            }
            capturePresentationCheckPending = true;
        }

        private void RunCaptureRefreshReasonProbe()
        {
            if(view==null)return;
            PrepareEmptyAIMatch();
            int beforeMulti=view.captureAnimationStartCount;
            bool built=game.TryPlayFromAI(0)&&game.TryPlayFromAI(19)&&
                game.TryPlayFromAI(1)&&game.TryPlayFromAI(20);
            int beforePass=game.moveCount;game.PassFromAI();
            built=built&&game.moveCount==beforePass+1&&game.TryPlayFromAI(2);
            multiCapturePresentationPassed=built&&game.board[0]==GoGame.EMPTY&&
                game.board[1]==GoGame.EMPTY&&view.captureAnimationStartCount==beforeMulti+1&&
                view.lastCaptureAnimationStoneCount==2&&
                view.lastRefreshReason==GoBoardView.REFRESH_CAPTURE;

            int beforeNewGame=view.captureAnimationStartCount;
            game.RequestNewGame();
            newGameNoCapturePresentationPassed=
                view.captureAnimationStartCount==beforeNewGame&&
                view.lastRefreshReason==GoBoardView.REFRESH_RESET;

            game.RequestStartMatch();game.TryPlayFromAI(0);game.TryPlayFromAI(1);
            int beforeForce=view.captureAnimationStartCount;
            game.RequestForceReset();
            forceResetNoCapturePresentationPassed=
                view.captureAnimationStartCount==beforeForce&&
                view.lastRefreshReason==GoBoardView.REFRESH_RESET;

            game.RequestStartMatch();game.TryPlayFromAI(0);game.TryPlayFromAI(1);
            int beforeDeserialize=view.captureAnimationStartCount;
            game.board[0]=GoGame.EMPTY;
            view.RefreshForReason(GoBoardView.REFRESH_DESERIALIZE);
            deserializeNoCapturePresentationPassed=
                view.captureAnimationStartCount==beforeDeserialize&&
                view.lastRefreshReason==GoBoardView.REFRESH_DESERIALIZE;
        }

        private void RunDoublePassProbe()
        {
            PrepareEmptyAIMatch();
            game.komiTimes2 = 15;
            bool blackMove = game.TryPlayFromAI(0);
            bool whiteMove = game.TryPlayFromAI(GoGame.AREA - 1);
            game.PassFromAI();
            game.PassFromAI();
            doublePassGameState = game.gameState;
            doublePassBlackArea = game.finalBlackArea;
            doublePassWhiteArea = game.finalWhiteArea;
            doublePassScore = game.finalWhiteMinusBlackScore;
            doublePassPassed = blackMove && whiteMove && game.consecutivePasses == 2 &&
                game.gameState != GoGame.STATE_PLAYING && game.moveCount == 4;
            exactAreaScorePassed = doublePassPassed && doublePassBlackArea == 1 &&
                doublePassWhiteArea == 1 && doublePassScore > 7.499f &&
                doublePassScore < 7.501f && game.winner == GoGame.WHITE &&
                game.GetChineseWhiteHandicapBonusTimes2() == 0 &&
                game.GetEffectiveChineseWhiteCompensationTimes2() == 15;
        }

        private void RunSuicideProbe()
        {
            PrepareEmptyAIMatch();
            bool b0 = game.TryPlayFromAI(0);
            bool w0 = game.TryPlayFromAI(179);
            bool b1 = game.TryPlayFromAI(2);
            bool w1 = game.TryPlayFromAI(181);
            bool b2 = game.TryPlayFromAI(3);
            bool w2 = game.TryPlayFromAI(161);
            bool b3 = game.TryPlayFromAI(4);
            bool w3 = game.TryPlayFromAI(199);
            bool suicide = game.TryPlayFromAI(180);
            suicideMoveCount = game.moveCount;
            suicidePassed = b0 && w0 && b1 && w1 && b2 && w2 && b3 && w3 &&
                !suicide && game.board[180] == GoGame.EMPTY &&
                game.moveCount == 8 && game.gameState == GoGame.STATE_PLAYING;
        }

        private void RunKoProbe()
        {
            PrepareEmptyAIMatch();
            bool previousSuperko = game.positionalSuperko;
            game.positionalSuperko = false;
            bool b0 = game.TryPlayFromAI(19);
            game.PassFromAI();
            bool b1 = game.TryPlayFromAI(21);
            game.PassFromAI();
            bool b2 = game.TryPlayFromAI(1);
            bool wTarget = game.TryPlayFromAI(20);
            game.PassFromAI();
            bool w0 = game.TryPlayFromAI(38);
            game.PassFromAI();
            bool w1 = game.TryPlayFromAI(40);
            game.PassFromAI();
            bool w2 = game.TryPlayFromAI(58);
            bool capture = game.TryPlayFromAI(39);
            koLocation = game.koLoc;
            koMoveCount = game.moveCount;
            bool recapture = game.TryPlayFromAI(20);
            koPassed = b0 && b1 && b2 && wTarget && w0 && w1 && w2 && capture &&
                !recapture && koLocation == 20 && koMoveCount == 13 &&
                game.board[39] == GoGame.BLACK && game.board[20] == GoGame.EMPTY;
            simpleKoPassed = koPassed && !game.IsRulesLegalMove(20);
            searchState.CopyFromGame(game);
            searchState.positionalSuperko = false;
            searchKoPassed = !searchState.IsLegalMove(20) && !searchState.Play(20);
            game.positionalSuperko = previousSuperko;
        }

        private void RunMultiSuicidePrisonerProbe()
        {
            PrepareEmptyAIMatch();
            bool ok = game.TryPlayFromAI(40);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(41);
            ok = ok && game.TryPlayFromAI(21);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(39);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(22);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(42);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(60);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(58);
            game.PassFromAI();
            ok = ok && game.TryPlayFromAI(78);
            ok = ok && game.TryPlayFromAI(59);
            whitePrisonersAfterSuicide = game.whiteCaptures;
            multiSuicidePrisonerPassed = ok && game.board[40] == GoGame.EMPTY &&
                game.board[41] == GoGame.EMPTY && game.board[59] == GoGame.EMPTY &&
                whitePrisonersAfterSuicide == 3 && game.gameState == GoGame.STATE_PLAYING;
        }

        private void RunPureRulesApiProbe()
        {
            PrepareEmptyAIMatch();
            game.matchStarted = false;
            pureRulesQueryPassed = game.IsRulesLegalMove(0) && !game.IsLegalMove(0);
            game.matchStarted = true;

            // Current-board score previews and presentation refreshes must not
            // overwrite the synchronized terminal result fields.
            game.finalBlackArea = 17;
            game.finalWhiteArea = 23;
            game.finalWhiteMinusBlackScore = 6.5f;
            game.ComputeCurrentWhiteMinusBlackAreaScore();
            if (game.ui != null) game.ui.RefreshNow();
            if (game.telemetry != null) game.telemetry.RefreshNow();
            previewFinalFieldsPassed = game.finalBlackArea == 17 &&
                game.finalWhiteArea == 23 &&
                game.finalWhiteMinusBlackScore > 6.499f &&
                game.finalWhiteMinusBlackScore < 6.501f;
        }

        private void RunCapacityProbe()
        {
            PrepareEmptyAIMatch();
            bool previousSuperko = game.positionalSuperko;
            game.positionalSuperko = false;
            // The production path verifies/replays history before accepting
            // a move, so changing moveCount alone is not a valid fixture.
            for(int i=0;i<GoGame.MAX_MOVES-1;i++)
                game.moveHistory[i]=1|((i&1)==1?512:0);
            game.moveCount = GoGame.MAX_MOVES - 1;
            game.positionHistoryCount = GoGame.MAX_POSITION_HISTORY - 1;
            game.sideToMove=GoGame.WHITE;
            game.consecutivePasses=GoGame.MAX_MOVES-1;
            game.lastMove=GoGame.PASS;
            game.koLoc=GoGame.NONE;
            for(int i=0;i<5;i++)
            {
                game.recentMoveLoc[i]=GoGame.PASS;
                game.recentMovePla[i]=(i&1)==0?GoGame.BLACK:GoGame.WHITE;
            }
            bool accepted = game.TryPlayFromAI(0);
            capacityPassed = accepted && game.moveCount == GoGame.MAX_MOVES &&
                game.positionHistoryCount == GoGame.MAX_POSITION_HISTORY &&
                game.gameState != GoGame.STATE_PLAYING;
            game.positionalSuperko = previousSuperko;
        }

        private void RunHandicapProbe()
        {
            game.RequestForceReset();
            bool placed = game.SetHandicap(5);
            handicapCount = game.handicapStones;
            int occupied = 0;
            for (int i = 0; i < GoGame.AREA; i++)
                if (game.board[i] != GoGame.EMPTY) occupied++;
            searchState.CopyFromGame(game);
            handicapPassed = placed && handicapCount == 5 && occupied == 5 &&
                game.sideToMove == GoGame.WHITE && game.moveCount == 0 &&
                game.gameState == GoGame.STATE_PLAYING &&
                game.GetChineseWhiteHandicapBonusTimes2() == 10 &&
                game.GetEffectiveChineseWhiteCompensationTimes2() == 25 &&
                game.GetEffectiveChineseWhiteCompensationPoints() > 12.499f &&
                game.GetEffectiveChineseWhiteCompensationPoints() < 12.501f &&
                searchState.komiTimes2 == 25;
        }

        private void RunResignProbe()
        {
            PrepareEmptyAIMatch();
            game.ResignFromAI();
            resignState = game.gameState;
            resignPassed = resignState == GoGame.STATE_RESIGNED &&
                game.winner == GoGame.WHITE && game.moveCount == 0;
        }

        private void BeginPositionalSuperkoProbe()
        {
            positionalSuperkoSetupIndex = 0;
            positionalSuperkoCycleIndex = 0;
            positionalSuperkoSetupOk = true;
            positionalSuperkoCycleOk = true;
            positionalSuperkoSetupSwitched = false;
            positionalSuperkoCandidateTested = false;
            positionalSuperkoFallbackTested = false;
            positionalSuperkoCandidateRejected = false;
            positionalSuperkoFallbackAccepted = false;
            // The setup is deliberately built without PSK so a triple-ko
            // cycle can be reached. Simple ko remains enabled by GoGame.
            game.positionalSuperko = false;
            PrepareEmptyAIMatch();
        }

        private void RunPositionalSuperkoStep()
        {
            if (positionalSuperkoSetupIndex < positionalSuperkoSetupMoves.Length)
            {
                bool accepted = PlayProbeMove(
                    positionalSuperkoSetupMoves[positionalSuperkoSetupIndex]);
                positionalSuperkoSetupOk = positionalSuperkoSetupOk && accepted;
                positionalSuperkoSetupIndex++;
                return;
            }
            if (!positionalSuperkoSetupSwitched)
            {
                game.positionalSuperko = true;
                positionalSuperkoSetupSwitched = true;
                return;
            }
            if (positionalSuperkoCycleIndex < positionalSuperkoCycleMoves.Length)
            {
                bool accepted = game.TryPlayFromAI(
                    positionalSuperkoCycleMoves[positionalSuperkoCycleIndex]);
                positionalSuperkoCycleOk = positionalSuperkoCycleOk && accepted;
                positionalSuperkoCycleIndex++;
                return;
            }
            if (!positionalSuperkoCandidateTested)
            {
                int moveCountBefore = game.moveCount;
                bool accepted = game.TryPlayFromAI(215);
                positionalSuperkoCandidateRejected = !accepted &&
                    game.moveCount == moveCountBefore && game.board[215] == GoGame.EMPTY &&
                    game.koLoc != 215;
                positionalSuperkoCandidateTested = true;
                return;
            }
            if (!positionalSuperkoFallbackTested)
            {
                game.positionalSuperko = false;
                bool accepted = game.TryPlayFromAI(215);
                positionalSuperkoFallbackAccepted = accepted &&
                    game.board[215] == GoGame.WHITE;
                game.positionalSuperko = true;
                positionalSuperkoFallbackTested = true;
                positionalSuperkoPassed = positionalSuperkoSetupOk &&
                    positionalSuperkoCycleOk && positionalSuperkoCandidateRejected &&
                    positionalSuperkoFallbackAccepted;
                phase++;
                return;
            }
            phase++;
        }

        private bool PlayProbeMove(int loc)
        {
            if (loc == GoGame.PASS)
            {
                int before = game.moveCount;
                game.PassFromAI();
                return game.moveCount == before + 1;
            }
            return game.TryPlayFromAI(loc);
        }
    }
}
