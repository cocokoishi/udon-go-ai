using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// World-space Go control panel. It deliberately uses Go terminology and
    /// search visits rather than chess-style depth controls.
    /// </summary>
    public sealed class GoUI : UdonSharpBehaviour
    {
        public GoGame game;
        public GoAiSettings aiSettings;
        public GoDifficultyProfile difficulty;
        public GoDifficultyProfile blackDifficulty;
        public GoDifficultyProfile whiteDifficulty;
        public GoAiController aiController;
        public GoTelemetry telemetry;
        // The generated production wall uses the same world-space UGUI/TMP
        // hierarchy as the Xiangqi product family.
        public TMP_Text titleText;
        public TMP_Text panelStatusText;
        public TMP_Text panelTurnText;
        public TMP_Text panelScoreText;
        public TMP_Text panelCaptureText;
        public TMP_Text panelSeatText;
        public TMP_Text panelControllerText;
        public TMP_Text panelStarterText;
        public TMP_Text panelActionText;
        public TMP_Text panelModeText;
        public TMP_Text panelDifficultyText;
        public TMP_Text panelSearchText;
        public TMP_Text panelHintText;
        public TMP_Text panelTelemetryText;
        public Image searchProgressFill;
        public TMP_Text panelSettingsText;
        // Dynamic summary shown directly below Apply Profile · Sync. It is
        // intentionally derived from the authoritative settings/mode snapshot
        // so late joiners see the same applied shared preset and per-side AI
        // levels rather than a local draft.
        public TMP_Text panelAppliedProfileText;
        public TMP_Text panelLanguageText;
        public TMP_Text undoButtonText;
        public TMP_Text drawButtonText;
        public TMP_Text advancedAnalysisButtonText;
        public TMP_Text aiHintPermissionButtonText;
        public TMP_Text aiMoveNowButtonText;
        public TMP_Text forceResetButtonText;
        // The generated settings wall exposes one independent numeric input
        // per colour. Custom is a visits-only override of the shared preset.
        // TMP_InputField gives VRChat a native keyboard/popup editing path and
        // avoids the imprecision of a 1..1226 world-space drag control.
        public TMP_InputField blackCustomVisitsInput;
        public TMP_InputField whiteCustomVisitsInput;
        public TMP_Text blackCustomVisitsValueText;
        public TMP_Text whiteCustomVisitsValueText;
        public Transform controlPanelMount;
        // Static labels are registered by the editor generator and replaced
        // in-place on language changes. This keeps English and Chinese from
        // being rendered as a stacked bilingual label in VR.
        public TMP_Text[] localizedTexts;
        public string[] chineseTexts;
        public string[] englishTexts;
        public Button[] controlButtons;
        public int[] controlButtonActions;
        public bool englishLanguage=true;
        public bool advancedAnalysisEnabled;
        public int refreshCount;
        public float lastRefreshMilliseconds;
        public float maxRefreshMilliseconds;
        private bool languageManuallySelected;
        private int displayedGameRevision=-1;
        private int displayedSettingsRevision=-1;
        private int displayedHintRevision=-1;
        private int displayedUndoOfferSide=GoGame.EMPTY;
        private int displayedUndoOfferRevision;
        private int displayedUndoOfferMoveCount=-1;
        private int displayedDrawOfferSide=GoGame.EMPTY;
        private int displayedDrawOfferRevision;
        private int displayedDrawOfferMoveCount=-1;
        private int displayedSearchPhase=-1;
        private int displayedSearchVisits=-1;
        private int displayedControllerState=-1;
        private int displayedAiOutputRevision=-1;
        private int displayedTelemetryRevision=-1;
        private int displayedDrawCooldown=-1;
        private int displayedForceResetCooldown=-1;
        private bool displayedForceResetArmed;
        private string displayedLastActionText="";
        private bool displayedLanguage;
        private bool hasDisplayedLanguage;
        private float nextDynamicRefreshTime;
        private int sharedPresetDraft=GoDifficultyProfile.BEGINNER;
        private bool blackUseCustomDraft;
        private bool whiteUseCustomDraft;
        private int blackCustomVisitsDraft=GoDifficultyProfile.BEGINNER_VISITS;
        private int whiteCustomVisitsDraft=GoDifficultyProfile.BEGINNER_VISITS;
        private bool settingsDraftDirty;
        private int draftSourceSettingsRevision=-1;
        private string observedBlackCustomVisitsInput="";
        private string observedWhiteCustomVisitsInput="";
        private float forceResetArmedUntil;
        private int forceResetArmedRevision=-1;
        public void Start()
        {
            if(aiSettings==null&&game!=null)aiSettings=game.aiSettings;
            if(game!=null&&game.aiSettings==null&&aiSettings!=null)game.aiSettings=aiSettings;
            // Udon sibling Start order is not deterministic.  Re-bind the
            // settings domain from the UI's serialized table references before
            // reading drafts or applying a transaction, so the local profile
            // views can never remain at their serialized Beginner defaults.
            if(aiSettings!=null)
            {
                if(aiSettings.game==null)aiSettings.game=game;
                GoDifficultyProfile resolvedBlack=blackDifficulty!=null?
                    blackDifficulty:aiSettings.blackDifficulty;
                GoDifficultyProfile resolvedWhite=whiteDifficulty!=null?
                    whiteDifficulty:aiSettings.whiteDifficulty;
                if(resolvedBlack!=null||resolvedWhite!=null)
                    aiSettings.RegisterProfiles(resolvedBlack,resolvedWhite);
            }
            englishLanguage=!IsChineseInterfaceLanguage(VRCPlayerApi.GetCurrentLanguage());
            advancedAnalysisEnabled=false;
            if(telemetry!=null)telemetry.showAdvancedAnalysis=false;
            InitializeSettingsDrafts();
            observedBlackCustomVisitsInput=blackCustomVisitsDraft.ToString();
            observedWhiteCustomVisitsInput=whiteCustomVisitsDraft.ToString();
            InvalidateRefreshCache();
            RefreshNow();
        }

        private void InitializeSettingsDrafts()
        {
            if(aiSettings!=null)
            {
                sharedPresetDraft=ClampSharedPreset(aiSettings.sharedPreset);
                blackUseCustomDraft=aiSettings.blackUseCustom;
                whiteUseCustomDraft=aiSettings.whiteUseCustom;
                blackCustomVisitsDraft=Mathf.Clamp(aiSettings.blackCustomVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
                whiteCustomVisitsDraft=Mathf.Clamp(aiSettings.whiteCustomVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
                settingsDraftDirty=false;
                draftSourceSettingsRevision=aiSettings.configRevision;
                return;
            }
            GoDifficultyProfile fallbackProfile=whiteDifficulty!=null?whiteDifficulty:blackDifficulty;
            sharedPresetDraft=fallbackProfile==null?GoDifficultyProfile.BEGINNER:
                ClampSharedPreset(fallbackProfile.followsShared?fallbackProfile.preset:
                    GoDifficultyProfile.BEGINNER);
            blackUseCustomDraft=blackDifficulty!=null&&!blackDifficulty.followsShared;
            whiteUseCustomDraft=whiteDifficulty!=null&&!whiteDifficulty.followsShared;
            blackCustomVisitsDraft=blackDifficulty==null?GoDifficultyProfile.BEGINNER_VISITS:
                Mathf.Clamp(blackDifficulty.maxVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            whiteCustomVisitsDraft=whiteDifficulty==null?GoDifficultyProfile.BEGINNER_VISITS:
                Mathf.Clamp(whiteDifficulty.maxVisits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            settingsDraftDirty=false;
            draftSourceSettingsRevision=game==null?-1:game.settingsRevision;
        }

        private void RefreshDraftFromAppliedIfClean()
        {
            int appliedRevision=GetAppliedSettingsRevision();
            if(game==null||settingsDraftDirty||draftSourceSettingsRevision==appliedRevision)return;
            InitializeSettingsDrafts();
            observedBlackCustomVisitsInput="";
            observedWhiteCustomVisitsInput="";
        }

        private int ClampSharedPreset(int value)
        {
            return value<GoDifficultyProfile.BEGINNER||value>GoDifficultyProfile.ULTRAHARD
                ?GoDifficultyProfile.BEGINNER:value;
        }

        private int GetAppliedSettingsRevision()
        {
            return aiSettings!=null?aiSettings.configRevision:(game==null?0:game.settingsRevision);
        }

        private void MarkSettingsDraftDirty()
        {
            settingsDraftDirty=true;
        }

        private void SetSharedPresetDraft(int preset)
        {
            sharedPresetDraft=ClampSharedPreset(preset);
            MarkSettingsDraftDirty();
        }

        private void SetDraftCustomVisits(int side,int visits)
        {
            visits=Mathf.Clamp(visits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            if(side==GoGame.BLACK)blackCustomVisitsDraft=visits;
            else if(side==GoGame.WHITE)whiteCustomVisitsDraft=visits;
            else return;
            MarkSettingsDraftDirty();
        }

        public void SetBlackFollowShared()
        {
            blackUseCustomDraft=false;
            MarkSettingsDraftDirty();RefreshNow();
        }

        public void SetBlackCustom()
        {
            blackUseCustomDraft=true;
            MarkSettingsDraftDirty();RefreshNow();
        }

        public void SetWhiteFollowShared()
        {
            whiteUseCustomDraft=false;
            MarkSettingsDraftDirty();RefreshNow();
        }

        public void SetWhiteCustom()
        {
            whiteUseCustomDraft=true;
            MarkSettingsDraftDirty();RefreshNow();
        }

        public void NotifyAppliedDifficultyChanged()
        {
            if(settingsDraftDirty)return;
            draftSourceSettingsRevision=-1;
            RefreshNow();
        }

        public override void OnLanguageChanged(string language)
        {
            if(languageManuallySelected)return;
            englishLanguage=!IsChineseInterfaceLanguage(language);
            InvalidateRefreshCache();
            RefreshNow();
            // The center analysis card is rendered by GoTelemetry rather than
            // by this presentation component. Refresh it explicitly so a
            // language change updates the live/final analysis immediately,
            // without waiting for another search or network revision.
            if(telemetry!=null)telemetry.RefreshNow();
        }

        private bool IsChineseInterfaceLanguage(string language)
        {
            return language=="zh-CN"||language=="zh-HK"||language=="zh-TW"||language=="zh-Hans"||language=="zh-Hant"||language=="zh";
        }

        private void Update()
        {
            PollCustomVisitsInputs();
            if(game==null)return;
            if(forceResetArmedUntil>0f&&!IsForceResetArmed())
            {
                forceResetArmedUntil=0f;
                forceResetArmedRevision=-1;
                RefreshNow();
            }
            // Force-reset permission is time based during a live turn. Poll
            // only the small integer countdown so the control becomes enabled
            // for every spectator at the synchronized 50-second deadline
            // without rebuilding the whole panel each frame.
            int forceResetCooldown=game.GetForceResetCooldownSeconds();
            bool forceResetArmed=IsForceResetArmed();
            if(forceResetCooldown!=displayedForceResetCooldown)
                RefreshNow();
            int searchPhase=aiController==null||aiController.search==null?-1:aiController.search.phase;
            int searchVisits=aiController==null||aiController.search==null?0:aiController.search.visitsCompleted;
            int controllerPhase=aiController==null?-1:aiController.controllerState;
            int outputRevision=aiController==null?-1:aiController.lastNeuralOutputRevision;
            int telemetryRevision=telemetry==null?-1:telemetry.revision;
            bool searchActive=searchPhase==GoMctsSearch.PHASE_WAITING_NEURAL||
                searchPhase==GoMctsSearch.PHASE_EXPANDING||searchPhase==GoMctsSearch.PHASE_SELECTING||
                searchPhase==GoMctsSearch.PHASE_BACKING_UP||
                searchPhase==GoMctsSearch.PHASE_RUNNING||
                searchPhase==GoMctsSearch.PHASE_COMPLETE||
                (aiController!=null&&aiController.hintRequested);
            bool phaseChanged=searchPhase!=displayedSearchPhase||
                controllerPhase!=displayedControllerState||outputRevision!=displayedAiOutputRevision||
                telemetryRevision!=displayedTelemetryRevision;
            float now=Time.realtimeSinceStartup;
            if(phaseChanged||(searchActive&&now>=nextDynamicRefreshTime))
                RefreshNow();
            else if(!searchActive&&game.GetDrawCooldownSeconds()!=displayedDrawCooldown)
                RefreshNow();
            // Search progress is intentionally sampled at a bounded cadence;
            // each Udon Tick may still run every frame, but it must not rebuild
            // every TMP string/UI control every frame.
            if(searchActive&&searchVisits!=displayedSearchVisits&&now>=nextDynamicRefreshTime)
                RefreshNow();
        }

        public void RefreshNow()
        {
            if(game==null)return;
            RefreshDraftFromAppliedIfClean();
            float refreshStartedAt=Time.realtimeSinceStartup;
            int searchPhase=aiController==null||aiController.search==null?-1:aiController.search.phase;
            int searchVisits=aiController==null||aiController.search==null?0:aiController.search.visitsCompleted;
            int controllerPhase=aiController==null?-1:aiController.controllerState;
            int outputRevision=aiController==null?-1:aiController.lastNeuralOutputRevision;
            int telemetryRevision=telemetry==null?-1:telemetry.revision;
            int appliedSettingsRevision=GetAppliedSettingsRevision();
            int drawCooldown=game.GetDrawCooldownSeconds();
            int forceResetCooldown=game.GetForceResetCooldownSeconds();
            bool forceResetArmed=IsForceResetArmed();
            bool searchActive=searchPhase==GoMctsSearch.PHASE_WAITING_NEURAL||
                searchPhase==GoMctsSearch.PHASE_EXPANDING||searchPhase==GoMctsSearch.PHASE_SELECTING||
                searchPhase==GoMctsSearch.PHASE_BACKING_UP||
                searchPhase==GoMctsSearch.PHASE_RUNNING||
                searchPhase==GoMctsSearch.PHASE_COMPLETE||
                (aiController!=null&&aiController.hintRequested);
            bool staticDirty=displayedGameRevision!=game.revision||
                displayedSettingsRevision!=appliedSettingsRevision||
                displayedHintRevision!=game.hintRevision||
                displayedUndoOfferSide!=game.undoOfferSide||
                displayedUndoOfferRevision!=game.undoOfferRevision||
                displayedUndoOfferMoveCount!=game.undoOfferMoveCount||
                displayedDrawOfferSide!=game.drawOfferSide||
                displayedDrawOfferRevision!=game.drawOfferRevision||
                displayedDrawOfferMoveCount!=game.drawOfferMoveCount||
                displayedLastActionText!=game.lastActionText||
                settingsDraftDirty||
                displayedDrawCooldown!=drawCooldown||
                displayedForceResetCooldown!=forceResetCooldown||
                displayedForceResetArmed!=forceResetArmed||
                !hasDisplayedLanguage||displayedLanguage!=englishLanguage;
            bool phaseDirty=searchPhase!=displayedSearchPhase||
                controllerPhase!=displayedControllerState||outputRevision!=displayedAiOutputRevision||
                telemetryRevision!=displayedTelemetryRevision;
            float now=Time.realtimeSinceStartup;
            bool progressDue=searchActive&&searchVisits!=displayedSearchVisits&&now>=nextDynamicRefreshTime;
            if(!staticDirty&&!phaseDirty&&!progressDue&&displayedGameRevision>=0)
            {FinishRefreshTiming(refreshStartedAt);return;}
            if(!hasDisplayedLanguage||displayedLanguage!=englishLanguage)
                ApplyStaticLanguage();
            refreshCount++;
            displayedGameRevision=game.revision;
            displayedSettingsRevision=appliedSettingsRevision;
            displayedHintRevision=game.hintRevision;
            displayedUndoOfferSide=game.undoOfferSide;
            displayedUndoOfferRevision=game.undoOfferRevision;
            displayedUndoOfferMoveCount=game.undoOfferMoveCount;
            displayedDrawOfferSide=game.drawOfferSide;
            displayedDrawOfferRevision=game.drawOfferRevision;
            displayedDrawOfferMoveCount=game.drawOfferMoveCount;
            displayedSearchPhase=searchPhase;
            displayedSearchVisits=searchVisits;
            displayedControllerState=controllerPhase;
            displayedAiOutputRevision=outputRevision;
            displayedTelemetryRevision=telemetryRevision;
            displayedDrawCooldown=drawCooldown;
            displayedForceResetCooldown=forceResetCooldown;
            displayedForceResetArmed=forceResetArmed;
            displayedLastActionText=game.lastActionText;
            displayedLanguage=englishLanguage;
            hasDisplayedLanguage=true;
            nextDynamicRefreshTime=now+0.16f;
            RefreshPanelPlacement();
            string turn=game.gameState==GoGame.STATE_PLAYING?(game.matchStarted?(game.sideToMove==GoGame.BLACK?(englishLanguage?"Black to play":"黑方行棋"):(englishLanguage?"White to play":"白方行棋")):(englishLanguage?"Ready to start":"等待开始")):(englishLanguage?"Game complete":"对局结束");
            string scoreText=GetDisplayedScoreText();
            float whiteCompensation=game.areaScoring
                ?game.GetEffectiveChineseWhiteCompensationPoints()
                :game.komiTimes2*0.5f;
            string compensationLabel=game.areaScoring&&game.handicapStones>0
                ?(englishLanguage?"  White comp ":"  白方补偿 ")
                :(englishLanguage?"  Komi ":"  贴目 ");
            string scoreLine=(englishLanguage?"W − B  ":"白 − 黑  ")+scoreText+
                compensationLabel+whiteCompensation.ToString("0.0");
            string captures=englishLanguage?"Captures  B "+game.blackCaptures+"  W "+game.whiteCaptures:"提子  黑 "+game.blackCaptures+"  白 "+game.whiteCaptures;
            GoDifficultyProfile active=GetDisplayedDifficulty();
            string difficultyLine=active==null?(englishLanguage?"AI  offline":"AI  离线"):(englishLanguage?"AI "+active.GetPresetLabelLocalized(true)+"  ·  "+active.maxVisits+" visits": "AI "+active.GetPresetLabelLocalized(false)+"  ·  "+active.maxVisits+" 次访问");
            if(settingsDraftDirty)
                difficultyLine+=(englishLanguage?"  ·  DRAFT (press Apply)":"  ·  待应用（请按应用）");
            string searchLine=aiController==null?(englishLanguage?"AI ready":"AI 就绪"):aiController.GetStatusTextLocalized(englishLanguage);
            string modeLine=GetModeLabel();
            string statusLine=game.GetStatusLineLocalized(englishLanguage);
            SetTextIfChanged(titleText,game.GetLocalizedBoardTitle(englishLanguage));
            SetTextIfChanged(panelStatusText,statusLine);
            SetTextIfChanged(panelTurnText,turn);
            SetTextIfChanged(panelScoreText,scoreLine);
            SetTextIfChanged(panelCaptureText,captures);
            SetTextIfChanged(panelSeatText,game.GetSeatLineLocalized(englishLanguage));
            SetTextIfChanged(panelControllerText,game.GetControllerLineLocalized(englishLanguage));
            SetTextIfChanged(panelStarterText,game.GetStarterLineLocalized(englishLanguage));
            string actionLine=forceResetArmed
                ?(englishLanguage
                    ?"Force reset clears the board and history · press again within 4 seconds"
                    :"强制重置会清空棋盘与历史 · 请在 4 秒内再次点击确认")
                :game.GetLastActionLocalized(englishLanguage);
            SetTextIfChanged(panelActionText,actionLine);
            SetTextIfChanged(undoButtonText,game.GetUndoActionLabel(englishLanguage));
            SetTextIfChanged(drawButtonText,game.GetDrawActionLabel(englishLanguage));
            SetTextIfChanged(advancedAnalysisButtonText,advancedAnalysisEnabled
                ?(englishLanguage?"ADVANCED ANALYSIS · ON":"高级分析 · 开")
                :(englishLanguage?"ADVANCED ANALYSIS · OFF":"高级分析 · 关"));
            SetTextIfChanged(panelModeText,modeLine);
            SetTextIfChanged(panelDifficultyText,difficultyLine);
            SetTextIfChanged(panelSearchText,searchLine);
            SetTextIfChanged(panelHintText,aiController==null?"AI Hint unavailable":aiController.GetHintStatusText(englishLanguage));
            SetTextIfChanged(aiHintPermissionButtonText,game.GetAiHintPermissionLabel(englishLanguage));
            SetTextIfChanged(aiMoveNowButtonText,aiController!=null&&aiController.HasCurrentEstimate()
                ?(englishLanguage?"AI MOVE NOW · USE ESTIMATE":"AI 立即落子 · 使用当前估计")
                :(englishLanguage?"AI MOVE NOW · WAITING ROOT":"AI 立即落子 · 等待根评估"));
            RefreshIndependentCustomInput(blackCustomVisitsInput,GoGame.BLACK,
                blackCustomVisitsDraft,blackUseCustomDraft,blackCustomVisitsValueText);
            RefreshIndependentCustomInput(whiteCustomVisitsInput,GoGame.WHITE,
                whiteCustomVisitsDraft,whiteUseCustomDraft,whiteCustomVisitsValueText);
            SetTextIfChanged(panelSettingsText,modeLine+"  ·  "+game.GetStarterLineLocalized(englishLanguage)+"  ·  "+game.GetNetworkHealthLocalized(englishLanguage));
            SetTextIfChanged(panelAppliedProfileText,GetAppliedProfileSummaryLocalized(englishLanguage));
            SetTextIfChanged(panelLanguageText,englishLanguage?"LANGUAGE-ENGLISH":"LANGUAGE-CHINESE");
            if(searchProgressFill!=null)
            {
                int progressTarget=searchPhase==GoMctsSearch.PHASE_WAITING_NEURAL||
                    searchPhase==GoMctsSearch.PHASE_EXPANDING||
                    searchPhase==GoMctsSearch.PHASE_SELECTING||
                    searchPhase==GoMctsSearch.PHASE_BACKING_UP||
                    searchPhase==GoMctsSearch.PHASE_RUNNING||
                    searchPhase==GoMctsSearch.PHASE_COMPLETE
                    ?(aiController==null||aiController.search==null?0:aiController.search.targetVisits)
                    :(telemetry!=null&&telemetry.hasFinalAnalysis?telemetry.finalAnalysisTargetVisits:0);
                int progressCompleted=searchPhase==GoMctsSearch.PHASE_WAITING_NEURAL||
                    searchPhase==GoMctsSearch.PHASE_EXPANDING||
                    searchPhase==GoMctsSearch.PHASE_SELECTING||
                    searchPhase==GoMctsSearch.PHASE_BACKING_UP||
                    searchPhase==GoMctsSearch.PHASE_RUNNING||
                    searchPhase==GoMctsSearch.PHASE_COMPLETE
                    ?searchVisits
                    :(telemetry!=null&&telemetry.hasFinalAnalysis?telemetry.finalAnalysisCompletedVisits:0);
                searchProgressFill.fillAmount=progressTarget>0
                    ?Mathf.Clamp01((float)progressCompleted/progressTarget):0f;
            }
            // GoTelemetry owns the center analysis text.  Writing a short UI
            // string here and immediately replacing it with the telemetry
            // string caused visible TMP flicker during active search.
            RefreshControlState();
            FinishRefreshTiming(refreshStartedAt);
        }

        private void FinishRefreshTiming(float startedAt)
        {
            lastRefreshMilliseconds=(Time.realtimeSinceStartup-startedAt)*1000f;
            if(lastRefreshMilliseconds>maxRefreshMilliseconds)
                maxRefreshMilliseconds=lastRefreshMilliseconds;
        }

        public void ResetRefreshTiming()
        {
            lastRefreshMilliseconds=0f;maxRefreshMilliseconds=0f;
        }

        public void InvalidateRefreshCache()
        {
            displayedGameRevision=-1;
            displayedSettingsRevision=-1;
            displayedHintRevision=-1;
            displayedUndoOfferSide=GoGame.EMPTY;
            displayedUndoOfferRevision=0;
            displayedUndoOfferMoveCount=-1;
            displayedDrawOfferSide=GoGame.EMPTY;
            displayedDrawOfferRevision=0;
            displayedDrawOfferMoveCount=-1;
            displayedSearchPhase=-1;
            displayedSearchVisits=-1;
            displayedControllerState=-1;
            displayedAiOutputRevision=-1;
            displayedTelemetryRevision=-1;
            displayedDrawCooldown=-1;
            displayedForceResetCooldown=-1;
            displayedForceResetArmed=false;
            displayedLastActionText="";
            forceResetArmedRevision=-1;
            hasDisplayedLanguage=false;
            nextDynamicRefreshTime=0f;
        }

        public void RefreshControlState()
        {
            if(controlButtons==null||controlButtonActions==null)return;
            int count=controlButtons.Length<controlButtonActions.Length?controlButtons.Length:controlButtonActions.Length;
            for(int i=0;i<count;i++)
            {
                Button button=controlButtons[i];
                if(button!=null)
                {
                    button.gameObject.SetActive(ShouldShowAction(controlButtonActions[i]));
                    button.interactable=CanInteract(controlButtonActions[i]);
                }
            }
            if(forceResetButtonText!=null)
            {
                bool armed=IsForceResetArmed();
                forceResetButtonText.text=armed
                    ?(englishLanguage?"PRESS AGAIN · CONFIRM RESET":"再次点击 · 确认重置")
                    :(englishLanguage?"FORCE RESET":"强制重置");
            }
        }

        private void SetTextIfChanged(TMP_Text target,string value)
        {
            if(target!=null&&target.text!=value)target.text=value;
        }

        private void RefreshIndependentCustomInput(TMP_InputField input,int side,int visits,
            bool useCustom,TMP_Text valueText)
        {
            if(input==null)return;
            visits=Mathf.Clamp(visits,1,GoMctsSearch.MAX_SUPPORTED_VISITS);
            input.interactable=useCustom&&game!=null&&game.CanLocalMatchControl();
            // Never overwrite a focused field while the player is typing. The
            // draft is sampled cooperatively and normalized when focus ends.
            if(!input.isFocused)
            {
                string normalized=visits.ToString();
                if(input.text!=normalized)input.text=normalized;
                if(side==GoGame.BLACK)observedBlackCustomVisitsInput=normalized;
                else if(side==GoGame.WHITE)observedWhiteCustomVisitsInput=normalized;
            }
            else if(side==GoGame.BLACK)observedBlackCustomVisitsInput=input.text;
            else if(side==GoGame.WHITE)observedWhiteCustomVisitsInput=input.text;
            if(valueText!=null)
                valueText.text=side==GoGame.BLACK
                    ?(englishLanguage?"BLACK CUSTOM  ":"黑方自定义  ")+visits
                    :(englishLanguage?"WHITE CUSTOM  ":"白方自定义  ")+visits;
        }

        private int ParseVisitsInput(string raw)
        {
            if(raw==null||raw.Length==0)return 0;
            int value=0;
            for(int i=0;i<raw.Length;i++)
            {
                char c=raw[i];
                if(c<'0'||c>'9')return 0;
                value=value*10+(c-'0');
                if(value>GoMctsSearch.MAX_SUPPORTED_VISITS)
                    return GoMctsSearch.MAX_SUPPORTED_VISITS;
            }
            return value;
        }

        private void PollCustomVisitsInputs()
        {
            if(blackCustomVisitsInput!=null)
            {
                string raw=blackCustomVisitsInput.text;
                int parsed=ParseVisitsInput(raw);
                if(parsed>0)
                {
                    if(raw!=observedBlackCustomVisitsInput)
                    {
                        observedBlackCustomVisitsInput=raw;
                        SetDraftCustomVisits(GoGame.BLACK,parsed);
                    }
                }
                else if(!blackCustomVisitsInput.isFocused)
                {
                    string normalized=Mathf.Clamp(blackCustomVisitsDraft,1,
                        GoMctsSearch.MAX_SUPPORTED_VISITS).ToString();
                    blackCustomVisitsInput.text=normalized;
                    observedBlackCustomVisitsInput=normalized;
                }
            }
            if(whiteCustomVisitsInput!=null)
            {
                string raw=whiteCustomVisitsInput.text;
                int parsed=ParseVisitsInput(raw);
                if(parsed>0)
                {
                    if(raw!=observedWhiteCustomVisitsInput)
                    {
                        observedWhiteCustomVisitsInput=raw;
                        SetDraftCustomVisits(GoGame.WHITE,parsed);
                    }
                }
                else if(!whiteCustomVisitsInput.isFocused)
                {
                    string normalized=Mathf.Clamp(whiteCustomVisitsDraft,1,
                        GoMctsSearch.MAX_SUPPORTED_VISITS).ToString();
                    whiteCustomVisitsInput.text=normalized;
                    observedWhiteCustomVisitsInput=normalized;
                }
            }
        }

        private string GetDisplayedScoreText()
        {
            if(game==null)return "—";
            if(game.HasTerminalBoardScore())
                return game.finalWhiteMinusBlackScore.ToString("+0.0;-0.0;0.0");
            if(game.gameState!=GoGame.STATE_PLAYING)return "—";
            if(telemetry!=null&&telemetry.HasCurrentRootScoreLead())
                return telemetry.GetCurrentRootScoreLeadWhiteMinusBlack()
                    .ToString("+0.0;-0.0;0.0");
            return "—";
        }

        private bool ShouldShowAction(int action)
        {
            if(game==null)return false;
            if(action==34)return !game.matchStarted;
            if(action==49)return game.GetAIControlCount()<2;
            if(action==50)return game.HasAnyAI()&&game.matchStarted&&game.IsAIControlled(game.sideToMove);
            if(action>=51&&action<=54)return game.GetAIControlCount()>0;
            // A human may request a real engine hint in PvP as well; only
            // AI-vs-AI has no human decision point to annotate.
            if(action==43)return game.GetAIControlCount()<2;
            if(action==35)return game.GetAIControlCount()==1;
            if(action==30)return !game.IsAIControlled(GoGame.BLACK);
            if(action==31)return !game.IsAIControlled(GoGame.WHITE);
            if(action==33)return game.GetLocalSeatSide()!=GoGame.EMPTY;
            if(action==38||action==39)return game.gameState==GoGame.STATE_PLAYING&&game.moveCount==0;
            return true;
        }

        private bool CanInteract(int action)
        {
            if(game==null)return false;
            bool playing=game.gameState==GoGame.STATE_PLAYING;
            bool started=game.matchStarted;
            bool aiTurn=game.IsAIControlled(game.sideToMove);
            int localSide=game.GetLocalSeatSide();
            bool localPlayer=Utilities.IsValid(Networking.LocalPlayer);
            bool localTurn=!localPlayer||localSide==game.sideToMove;
            bool seated=!localPlayer||localSide!=GoGame.EMPTY;
            bool controller=game.CanLocalMatchControl();
            bool forceResetController=game.CanLocalForceReset();
            bool liveStructureLocked=game.matchStarted&&
                game.gameState==GoGame.STATE_PLAYING&&!forceResetController;
            if(action==22||action==48)return true;
            if(action==40)return forceResetController;
            if(action==0||action==3||action==4||action==5||action==6||action==35)return controller&&!liveStructureLocked;
            if(action==1||action==2)return playing&&started&&!aiTurn&&localTurn;
            if(action==10||action==11||action==12||action==13||action==23)return game.GetAIControlCount()>0&&controller;
            if(action==20)return controller&&!liveStructureLocked;
            if(action==21)return controller&&!liveStructureLocked;
            // Keep human-seat buttons clickable even when the seat is already
            // occupied.  The authoritative ClaimSide path returns the precise
            // "seat occupied" message; disabling the control locally made a
            // late joiner look as if the entire table UI was dead.
            if(action==30)return !game.IsAIControlled(GoGame.BLACK);
            if(action==31)return !game.IsAIControlled(GoGame.WHITE);
            if(action==32)return !started&&game.GetAIControlCount()<2;
            if(action==33)return seated;
            if(action==34)return playing&&!started&&controller;
            if(action==38||action==39)return playing&&game.moveCount==0&&
                controller&&!liveStructureLocked;
            if(action==43)return playing&&started&&game.aiHintsEnabled&&
                game.aiHintPermissionLocked&&localTurn&&!aiTurn&&
                (aiController!=null);
            if(action==49)return playing&&!started&&!game.aiHintPermissionLocked&&controller;
            if(action==50)return playing&&started&&aiTurn&&controller&&
                aiController!=null&&aiController.HasCurrentEstimate();
            if(action>=51&&action<=54)return controller&&game.GetAIControlCount()>0;
            if(action==44)return game.CanLocalUndo();
            if(action==45)return playing&&started&&game.GetAIControlCount()==0&&
                seated&&game.GetDrawCooldownSeconds()<=0;
            return true;
        }

        private void RefreshPanelPlacement()
        {
            if(controlPanelMount==null||game==null)return;
            int aiCount=game.GetAIControlCount();
            // Keep the Xiangqi family behaviour only for the true two-human
            // table: AI tables stay on the rear wall, while PvP moves the
            // console to the side so two players can face the board.
            // PvP is the only side-facing layout.  Every AI-containing mode
            // stays on the rear wall, including AIvAI; rotating AIvAI into
            // the PvP position makes the generated control surface disagree
            // with the mode and with the Xiangqi-family gallery layout.
            float yaw=aiCount==0?90f:0f;
            controlPanelMount.localRotation=Quaternion.Euler(0f,yaw,0f);
        }

        public void HandleAction(int action)
        {
            // GoGame is the only authority that knows whether the command was
            // accepted.  Do not invalidate a search from the presentation
            // layer: a denied spectator click must not cancel the owner's
            // valid session, and accepted commands invalidate exactly once in
            // their authoritative mutation path.
            if(action==0){if(game!=null)game.NewGame();}
            else if(action==1){if(game!=null)game.Pass();}
            else if(action==2){if(game!=null)game.Resign();}
            else if(action==44){if(game!=null)game.RequestUndo();}
            else if(action==45){if(game!=null)game.OfferOrAcceptDraw();}
            else if(action==48)ToggleAdvancedAnalysis();
            else if(action==49)ToggleAiHintPermission();
            else if(action==50&&aiController!=null)aiController.CommitCurrentEstimate();
            else if(action==51)SetBlackFollowShared();
            else if(action==52)SetBlackCustom();
            else if(action==53)SetWhiteFollowShared();
            else if(action==54)SetWhiteCustom();
            else if(action>=3&&action<=6&&aiController!=null)aiController.SetMode(action-3);
            else if(action>=10&&action<=13)ApplyPresetToShared(action-10);
            else if(action==20&&aiController!=null)aiController.SetAiColor(GoGame.BLACK);
            else if(action==21&&aiController!=null)aiController.SetAiColor(GoGame.WHITE);
            else if(action==22)ToggleLanguage();
            else if(action==23)ApplyAISettings();
            else if(action==30)JoinBlack();
            else if(action==31)JoinWhite();
            else if(action==32)JoinHuman();
            else if(action==33)LeaveSeat();
            else if(action==34)StartMatch();
            else if(action==35)SwapSides();
            else if(action==38)SetBlackStarts();
            else if(action==39)SetWhiteStarts();
            else if(action==40)ForceReset();
            else if(action==43&&aiController!=null)aiController.ToggleHint();
            else if(action>=1000&&action<1000+GoGame.AREA&&game!=null)game.TryPlay(action-1000);
            RefreshNow();
        }

        public void Press(){RefreshNow();}

        public void ToggleLanguage()
        {
            languageManuallySelected=true;
            englishLanguage=!englishLanguage;
            InvalidateRefreshCache();
            RefreshNow();
            // GoTelemetry owns panelTelemetryText, so GoUI.RefreshNow alone
            // cannot switch the center analysis language.
            if(telemetry!=null)telemetry.RefreshNow();
        }

        public void ToggleAdvancedAnalysis()
        {
            advancedAnalysisEnabled=!advancedAnalysisEnabled;
            if(advancedAnalysisEnabled&&game!=null&&game.boardPool!=null)
                game.boardPool.RefreshMemoryAccounting();
            if(telemetry!=null)
            {
                telemetry.showAdvancedAnalysis=advancedAnalysisEnabled;
                telemetry.RefreshNow();
            }
            InvalidateRefreshCache();
            RefreshNow();
        }

        public void ToggleAiHintPermission(){if(game!=null)game.ToggleAiHintPermission();}

        public void JoinBlack(){if(game!=null)game.ClaimBlack();}
        public void JoinWhite(){if(game!=null)game.ClaimWhite();}
        public void JoinHuman(){if(game!=null)game.ClaimHuman();}
        public void LeaveSeat(){if(game!=null)game.ReleaseLocalSeat();}
        public void StartMatch(){if(game!=null)game.RequestStartMatch();}
        public void ForceReset()
        {
            if(game==null)return;
            // Match the Xiangqi interaction contract: an eligible user must
            // press the destructive reset control twice within four seconds.
            // An ineligible spectator gets the authoritative denial message
            // and can never arm a reset locally.
            if(!game.CanLocalForceReset())
            {
                forceResetArmedUntil=0f;
                forceResetArmedRevision=-1;
                game.RequestForceReset();
                RefreshNow();
                return;
            }
            if(!IsForceResetArmed())
            {
                forceResetArmedUntil=Time.time+4f;
                forceResetArmedRevision=game.revision;
                if(forceResetButtonText!=null)
                    forceResetButtonText.text=englishLanguage
                        ?"PRESS AGAIN · CONFIRM RESET":"再次点击 · 确认重置";
                SetTextIfChanged(panelActionText,englishLanguage
                    ?"Force reset clears the board and history · press again within 4 seconds"
                    :"强制重置会清空棋盘与历史 · 请在 4 秒内再次点击确认");
                RefreshControlState();
                return;
            }
            forceResetArmedUntil=0f;
            forceResetArmedRevision=-1;
            game.RequestForceReset();
            RefreshNow();
        }
        public void RequestUndo(){if(game!=null)game.RequestUndo();}
        public void OfferOrAcceptDraw(){if(game!=null)game.OfferOrAcceptDraw();}
        public void SwapSides(){if(game!=null)game.SwapHumanSideAndReset();}
        public void SetBlackStarts(){if(game!=null)game.SetBlackStarts();}
        public void SetWhiteStarts(){if(game!=null)game.SetWhiteStarts();}
        public void PresetBeginner(){SetSharedPresetDraft(GoDifficultyProfile.BEGINNER);RefreshNow();}
        public void PresetAdvanced(){SetSharedPresetDraft(GoDifficultyProfile.ADVANCED);RefreshNow();}
        public void PresetMaster(){SetSharedPresetDraft(GoDifficultyProfile.MASTER);RefreshNow();}
        public void PresetUltraHard(){SetSharedPresetDraft(GoDifficultyProfile.ULTRAHARD);RefreshNow();}
        public void ApplyAISettings()
        {
            // A player can finish typing and press Apply in the same frame.
            // Do not rely solely on Update() having sampled the UI control;
            // capture the current value at the commit boundary as well.
            PollCustomVisitsInputs();
            if(aiSettings!=null)
            {
                int requestedSharedPreset=ClampSharedPreset(sharedPresetDraft);
                int packedBlackVisits=Mathf.Clamp(blackCustomVisitsDraft,1,
                    GoMctsSearch.MAX_SUPPORTED_VISITS);
                int packedWhiteVisits=Mathf.Clamp(whiteCustomVisitsDraft,1,
                    GoMctsSearch.MAX_SUPPORTED_VISITS);
                if(!blackUseCustomDraft)packedBlackVisits=-packedBlackVisits;
                if(!whiteUseCustomDraft)packedWhiteVisits=-packedWhiteVisits;
                // Commit the complete draft as one integer-only transaction.
                // Do not queue a staging event and a second commit event in
                // the same frame: Udon event ordering is not guaranteed across
                // behaviours, and the commit could observe the previous draft.
                aiSettings.ApplyPackedVisits(requestedSharedPreset,
                    packedBlackVisits,packedWhiteVisits);
                if(aiSettings.lastApplySucceeded)InitializeSettingsDrafts();
                RefreshNow();
                return;
            }
            // Standalone fixtures without GoAiSettings retain the local
            // profile command path, but it still applies only the shared
            // preset or a visits-only side override.
            GoDifficultyProfile p=GetDisplayedDifficulty();
            if(p!=null)
            {
                p.ApplyResolvedLocal(sharedPresetDraft,
                    p.settingsSide==GoGame.BLACK?blackCustomVisitsDraft:whiteCustomVisitsDraft,
                    GetAppliedSettingsRevision(),p.settingsSide==GoGame.BLACK?blackUseCustomDraft:whiteUseCustomDraft);
                draftSourceSettingsRevision=GetAppliedSettingsRevision();
                settingsDraftDirty=false;
            }
            RefreshNow();
        }

        private void ApplyPresetToShared(int preset)
        {
            // Preset buttons edit local draft state. Only Apply Profile · Sync
            // mutates the synchronized profile and bumps configuration identity.
            // Sample both visible Custom inputs first so a player can type a
            // value and immediately choose a shared preset without RefreshNow
            // restoring the previous draft before the next Update tick.
            PollCustomVisitsInputs();
            SetSharedPresetDraft(preset);
            RefreshNow();
        }

        private void ApplyStaticLanguage()
        {
            if(localizedTexts==null||chineseTexts==null||englishTexts==null)return;
            int count=localizedTexts.Length;
            if(chineseTexts.Length<count)count=chineseTexts.Length;
            if(englishTexts.Length<count)count=englishTexts.Length;
            for(int i=0;i<count;i++)
                if(localizedTexts[i]!=null)
                {
                    string value=englishLanguage?englishTexts[i]:chineseTexts[i];
                    // A schema-9 scene can still carry a legacy serialized
                    // difficulty label until the editor generator is run
                    // again. Normalize that display-only text at the boundary
                    // so players never see the old alias or casing; the
                    // authoritative preset/visit values remain untouched.
                    value=NormalizeStaticDifficultyLabel(value);
                    if(localizedTexts[i].text!=value)localizedTexts[i].text=value;
                }
        }

        private string NormalizeStaticDifficultyLabel(string value)
        {
            if(value==null)return "";
            // Older generated scenes used several bilingual/typographic
            // spellings.  Normalize only presentation text at this boundary;
            // the authoritative preset enum and visit budget are untouched.
            value=value.Replace("\u6781\u96BE(Ultrahard)","Ultrahard");
            value=value.Replace("\u6781\u96BE (Ultrahard)","Ultrahard");
            value=value.Replace("\u6781\u96BE（Ultrahard）","Ultrahard");
            value=value.Replace("\u6781\u96BE - Ultrahard","Ultrahard");
            value=value.Replace("\u6781\u96BE","Ultrahard");
            value=value.Replace("ULTRAHARD","Ultrahard");
            value=value.Replace("UltraHard","Ultrahard");
            return value;
        }

        private GoDifficultyProfile GetDisplayedDifficulty()
        {
            if(game!=null)
            {
                if(game.sideToMove==GoGame.BLACK&&blackDifficulty!=null)return blackDifficulty;
                if(game.sideToMove==GoGame.WHITE&&whiteDifficulty!=null)return whiteDifficulty;
                if(game.sideToMove==GoGame.BLACK&&aiController!=null&&aiController.blackDifficulty!=null)return aiController.blackDifficulty;
                if(game.sideToMove==GoGame.WHITE&&aiController!=null&&aiController.whiteDifficulty!=null)return aiController.whiteDifficulty;
            }
            if(aiController!=null&&aiController.GetActiveDifficulty()!=null)return aiController.GetActiveDifficulty();
            return difficulty;
        }

        public string GetModeLabel()
        {
            if(game==null)return "PvAI · White AI";
            return game.GetMatchModeName(englishLanguage)+" · "+game.GetControllerLineLocalized(englishLanguage);
        }

        public string GetAppliedProfileSummaryLocalized(bool english)
        {
            if(game==null)return english?"APPLIED · no table":"已应用 · 无棋桌";
            int shared=aiSettings==null?GoDifficultyProfile.BEGINNER:
                ClampSharedPreset(aiSettings.sharedPreset);
            string sharedLabel=GetPresetLabelForValue(shared,english);
            string black=GetAppliedSideSummary(GoGame.BLACK,english);
            string white=GetAppliedSideSummary(GoGame.WHITE,english);
            return english
                ?"APPLIED · SHARED "+sharedLabel+" · BLACK "+black+" · WHITE "+white
                :"已应用 · 共享 "+sharedLabel+" · 黑方 "+black+" · 白方 "+white;
        }

        private string GetAppliedSideSummary(int side,bool english)
        {
            if(!game.IsAIControlled(side))return english?"PLAYER":"玩家";
            // Read the synchronized domain first. The profile components are
            // resolved mirrors and may be one Udon event behind on a late
            // join; the summary must never display that stale local view.
            if(aiSettings!=null)
            {
                int visits=aiSettings.GetEffectiveVisits(side);
                if(aiSettings.UsesCustom(side))
                    return english?"Custom "+visits+" visits":"自定义 "+visits+" 次搜索";
                return GetPresetLabelForValue(aiSettings.sharedPreset,english)+" "+visits+
                    (english?" visits":" 次搜索");
            }
            GoDifficultyProfile profile=side==GoGame.BLACK?blackDifficulty:whiteDifficulty;
            if(profile!=null)
                return profile.GetPresetLabelLocalized(english)+" "+profile.maxVisits+
                    (english?" visits":" 次搜索");
            return GetPresetLabelForValue(GoDifficultyProfile.BEGINNER,english)+" "+
                GoDifficultyProfile.BEGINNER_VISITS+(english?" visits":" 次搜索");
        }

        private string GetPresetLabelForValue(int preset,bool english)
        {
            if(preset==GoDifficultyProfile.ADVANCED)return english?"Advanced":"进阶";
            if(preset==GoDifficultyProfile.MASTER)return english?"Master":"大师";
            if(preset==GoDifficultyProfile.ULTRAHARD)return "Ultrahard";
            return english?"Beginner":"入门";
        }

        private bool IsForceResetArmed()
        {
            return forceResetArmedUntil>0f&&Time.time<forceResetArmedUntil&&
                game!=null&&forceResetArmedRevision==game.revision;
        }
    }
}
