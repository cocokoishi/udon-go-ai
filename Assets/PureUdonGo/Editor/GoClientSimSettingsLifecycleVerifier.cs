#if UNITY_EDITOR
using System;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Regression gate for the one-domain settings contract. It exercises the
    /// real generated UGUI/Udon path and verifies Follow Shared/Custom
    /// resolution plus an Apply while an 8-visit search is active.
    /// </summary>
    public static class GoClientSimSettingsLifecycleVerifier
    {
        private const double TimeoutSeconds=900.0;
        private const string PendingKey="PureUdonGo.ClientSimSettingsLifecycle.Pending";
        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int stage;
        private static int stageFrames;
        private static int activeSearchToken;
        private static int activeSearchTargetVisits;
        private static int applyBaselineRevision;
        private static int applyBaselineSerializations;
        private static UdonBehaviour uiProgram;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour settingsProgram;
        private static UdonBehaviour blackProfileProgram;
        private static UdonBehaviour whiteProfileProgram;
        private static GoUI uiProxy;
        private static TMP_InputField blackVisitsInput;
        private static TMP_InputField whiteVisitsInput;
        private static int remoteBaselineToken;
        private static int remoteBaselineTarget;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if(!SessionState.GetBool(PendingKey,false))return;
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            EditorApplication.update+=Pump;
        }

        [MenuItem("Tools/Pure Udon Go/Verify Settings Lifecycle And Follow Shared")]
        public static void VerifySettingsLifecycle()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded.");
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,displayLogs=true,deleteEditorOnly=false,
                spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,
                localPlayerIsMaster=true,isInstanceOwner=true,initializationDelay=0f,
                currentLanguage="en"
            });
            SessionState.SetBool(PendingKey,true);
            EditorSceneManager.SetActiveScene(scene);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SETTINGS_START scene="+Editor.GoWorldGenerator.ScenePath);
            EditorApplication.isPlaying=true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            SessionState.SetBool(PendingKey,false);
            deadline=0.0;enteredPlayMode=false;resultWritten=false;stage=0;stageFrames=0;activeSearchToken=0;
            activeSearchTargetVisits=0;
            uiProgram=null;gameProgram=null;aiProgram=null;searchProgram=null;settingsProgram=null;
            applyBaselineRevision=0;applyBaselineSerializations=0;
            blackProfileProgram=null;whiteProfileProgram=null;uiProxy=null;settingsProgram=null;
            blackVisitsInput=null;whiteVisitsInput=null;
            remoteBaselineToken=0;remoteBaselineTarget=0;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)enteredPlayMode=true;
            else if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(PendingKey,false))
                Fail("PlayMode ended before settings lifecycle gate completed");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance()){FailIfTimedOut("ClientSim did not initialize");return;}
                ResolvePrograms();
                if(uiProgram==null||gameProgram==null||aiProgram==null||searchProgram==null||settingsProgram==null||
                    blackProfileProgram==null||whiteProfileProgram==null||blackVisitsInput==null||whiteVisitsInput==null)
                { FailIfTimedOut("generated settings programs or controls were not found");return; }
                stageFrames++;
                if(stage==0)
                {
                    if(ReadInt(settingsProgram,"sharedPreset")!=GoDifficultyProfile.BEGINNER||
                        ReadBool(settingsProgram,"blackUseCustom")||ReadBool(settingsProgram,"whiteUseCustom"))
                    {Fail("settings domain did not start at shared Beginner/8 Follow Shared");return;}
                    stage=1;stageFrames=0;return;
                }
                if(stage==1)
                {
                    Press(FindButton(53),"WHITE FOLLOW SHARED");
                    Press(FindButton(52),"BLACK CUSTOM");
                    blackVisitsInput.text="64";
                    Press(FindButton(11),"SHARED ADVANCED 20");
                    applyBaselineRevision=ReadInt(settingsProgram,"configRevision");
                    applyBaselineSerializations=ReadInt(settingsProgram,"serializationCount");
                    Press(FindButton(23),"APPLY ALL SETTINGS");
                    stage=2;stageFrames=0;return;
                }
                if(stage==2)
                {
                    if(ReadInt(settingsProgram,"sharedPreset")==GoDifficultyProfile.ADVANCED&&
                        ReadInt(settingsProgram,"configRevision")==applyBaselineRevision+1&&
                        ReadInt(settingsProgram,"serializationCount")==applyBaselineSerializations+1&&
                        ReadBool(settingsProgram,"blackUseCustom")&&ReadInt(settingsProgram,"blackCustomVisits")==64&&
                        !ReadBool(settingsProgram,"whiteUseCustom")&&
                        MatchesProfile(blackProfileProgram,GoDifficultyProfile.CUSTOM,64,5,
                            1.31860f,0.10f,201,-0.97058f)&&
                        MatchesProfile(whiteProfileProgram,GoDifficultyProfile.ADVANCED,
                            GoDifficultyProfile.ADVANCED_VISITS,2,1.60f,0.10f,64,-1f))
                    {stage=3;stageFrames=0;return;}
                    FailIfTimedOut("black custom/white Follow Shared did not resolve atomically");return;
                }
                if(stage==3)
                {
                    Press(FindButton(12),"SHARED MASTER 48");
                    Press(FindButton(23),"APPLY MASTER");
                    stage=4;stageFrames=0;return;
                }
                if(stage==4)
                {
                    if(ReadInt(settingsProgram,"sharedPreset")==GoDifficultyProfile.MASTER&&
                        ReadBool(settingsProgram,"blackUseCustom")&&
                        MatchesProfile(blackProfileProgram,GoDifficultyProfile.CUSTOM,64,5,
                            1.31860f,0.10f,201,-0.97058f)&&
                        !ReadBool(settingsProgram,"whiteUseCustom")&&
                        MatchesProfile(whiteProfileProgram,GoDifficultyProfile.MASTER,
                            GoDifficultyProfile.MASTER_VISITS,4,1.35f,0.10f,128,-0.98f))
                    {
                        // Case F: both sides may carry independent custom
                        // values. Changing the shared preset must not erase
                        // the black custom value, and a white custom Apply
                        // must persist separately.
                        Press(FindButton(54),"WHITE CUSTOM");
                        whiteVisitsInput.text="96";
                        Press(FindButton(23),"APPLY BOTH CUSTOM");
                        stage=5;stageFrames=0;return;
                    }
                    FailIfTimedOut("shared preset changed a black custom side");return;
                }
                if(stage==5)
                {
                    if(ReadBool(settingsProgram,"blackUseCustom")&&
                        ReadInt(settingsProgram,"blackCustomVisits")==64&&
                        MatchesProfile(blackProfileProgram,GoDifficultyProfile.CUSTOM,64,5,
                            1.31860f,0.10f,201,-0.97058f)&&
                        ReadBool(settingsProgram,"whiteUseCustom")&&
                        ReadInt(settingsProgram,"whiteCustomVisits")==96&&
                        MatchesProfile(whiteProfileProgram,GoDifficultyProfile.CUSTOM,96,7,
                            1.27435f,0.10f,305,-0.95731f))
                    {
                        Press(FindButton(10),"SHARED BEGINNER 8");
                        Press(FindButton(51),"BLACK FOLLOW SHARED");
                        Press(FindButton(53),"WHITE FOLLOW SHARED");
                        Press(FindButton(23),"RESTORE BEGINNER");
                        stage=6;stageFrames=0;return;
                    }
                    FailIfTimedOut("both custom black/white visits did not persist independently");return;
                }
                if(stage==6)
                {
                    // A simulated incoming settings snapshot exercises the
                    // same backing Udon variables and OnDeserialization path
                    // used by a remote client. It must resolve deterministically
                    // and must not retain poisoned local knobs.
                    blackProfileProgram.SetProgramVariable("cpuct",9.5f);
                    blackProfileProgram.SetProgramVariable("moveTemperature",9.5f);
                    blackProfileProgram.SetProgramVariable("policyTopK",1);
                    whiteProfileProgram.SetProgramVariable("cpuct",9.5f);
                    whiteProfileProgram.SetProgramVariable("moveTemperature",9.5f);
                    whiteProfileProgram.SetProgramVariable("policyTopK",1);
                    int incomingRevision=ReadInt(settingsProgram,"configRevision")+1;
                    settingsProgram.SetProgramVariable("sharedPreset",GoDifficultyProfile.BEGINNER);
                    settingsProgram.SetProgramVariable("blackUseCustom",false);
                    settingsProgram.SetProgramVariable("whiteUseCustom",false);
                    settingsProgram.SetProgramVariable("blackCustomVisits",17);
                    settingsProgram.SetProgramVariable("whiteCustomVisits",19);
                    settingsProgram.SetProgramVariable("configRevision",incomingRevision);
                    settingsProgram.SendCustomEvent("_onDeserialization");
                    stage=7;stageFrames=0;return;
                }
                if(stage==7)
                {
                    if(ReadInt(settingsProgram,"sharedPreset")==GoDifficultyProfile.BEGINNER&&
                        !ReadBool(settingsProgram,"blackUseCustom")&&
                        MatchesProfile(blackProfileProgram,GoDifficultyProfile.BEGINNER,
                            GoDifficultyProfile.BEGINNER_VISITS,1,1.85f,0.10f,24,-1f)&&
                        MatchesProfile(whiteProfileProgram,GoDifficultyProfile.BEGINNER,
                            GoDifficultyProfile.BEGINNER_VISITS,1,1.85f,0.10f,24,-1f))
                    {
                        // The generated default is PvAI with Black to move.  Select
                        // White starts through the real UI/Udon command so the AI
                        // owns the first turn and the lifecycle gate can observe a
                        // live 8-visit search rather than an idle human turn.
                        uiProgram.SendCustomEvent("SetWhiteStarts");
                        Press(FindButton(34),"START MATCH");
                        stage=8;stageFrames=0;return;
                    }
                    FailIfTimedOut("remote settings deserialization did not restore complete Beginner profiles");return;
                }
                if(stage==8)
                {
                    int phase=ReadInt(searchProgram,"phase");
                    int token=ReadInt(searchProgram,"searchToken");
                    // Apply on the first pump after StartMatch.  The production
                    // pipeline is pre-warmed, so waiting several editor frames
                    // can legitimately let an 8-visit search finish before the
                    // gate observes its active SearchSession.
                    if(stageFrames<1)return;
                    bool active=phase==GoMctsSearch.PHASE_WAITING_NEURAL||
                        phase==GoMctsSearch.PHASE_EXPANDING||phase==GoMctsSearch.PHASE_SELECTING||
                        phase==GoMctsSearch.PHASE_BACKING_UP||phase==GoMctsSearch.PHASE_RUNNING;
                    if(!active)
                    {
                        // StartMatch and the first AI tick are separate Udon
                        // events. Keep waiting for the real SearchSession
                        // instead of treating the transient idle phase as a
                        // product failure.
                        FailIfTimedOut("settings lifecycle gate could not observe the active 8-visit search");
                        return;
                    }
                    activeSearchToken=token;
                    activeSearchTargetVisits=ReadInt(searchProgram,"targetVisits");
                    Press(FindButton(11),"SHARED ADVANCED DRAFT");
                    Press(FindButton(23),"APPLY ADVANCED NEXT SEARCH");
                    stage=9;stageFrames=0;return;
                }
                if(stage==9)
                {
                    int currentToken=ReadInt(searchProgram,"searchToken");
                    int target=ReadInt(searchProgram,"targetVisits");
                    if(activeSearchToken!=0&&currentToken!=0&&currentToken!=activeSearchToken)
                    {
                        // The admitted-frame pump can finish an 8-visit search
                        // before this editor-side sampling callback runs. A
                        // token change is valid only when the exact captured
                        // target completed; it is not evidence that Apply
                        // interrupted the session.
                        int lastTarget=ReadInt(aiProgram,"lastSearchTargetVisits");
                        int lastCompleted=ReadInt(aiProgram,"lastSearchCompletedVisits");
                        if(lastTarget!=activeSearchTargetVisits||
                            lastCompleted!=activeSearchTargetVisits)
                        {Fail("Apply settings cancelled or replaced the active SearchSession");return;}
                    }
                    else
                    {
                        if(target!=GoDifficultyProfile.BEGINNER_VISITS)
                        {Fail("active search target changed from captured Beginner/8 settings");return;}
                        if(ReadInt(searchProgram,"visitsCompleted")<GoDifficultyProfile.BEGINNER_VISITS)
                        {FailIfTimedOut("active search did not complete after configuration Apply");return;}
                    }
                    // Enter AIvAI through the visible mode control. The next
                    // real search must capture the newly applied Advanced
                    // profile instead of inheriting the old Beginner target.
                    Press(FindButton(6),"AIvAI next real search");
                    Press(FindButton(34),"START AIvAI");
                    stage=10;stageFrames=0;return;
                }
                if(stage==10)
                {
                    int phase=ReadInt(searchProgram,"phase");
                    int token=ReadInt(searchProgram,"searchToken");
                    bool active=phase==GoMctsSearch.PHASE_WAITING_NEURAL||phase==GoMctsSearch.PHASE_EXPANDING||
                        phase==GoMctsSearch.PHASE_SELECTING||phase==GoMctsSearch.PHASE_BACKING_UP||phase==GoMctsSearch.PHASE_RUNNING;
                    if(active&&token!=activeSearchToken)
                    {
                        if(ReadInt(searchProgram,"targetVisits")!=GoDifficultyProfile.ADVANCED_VISITS)
                        {Fail("next real search did not capture newly applied Advanced visits");return;}
                        remoteBaselineToken=token;remoteBaselineTarget=ReadInt(searchProgram,"targetVisits");
                        // Repeat the same update through a remote-style
                        // deserialization while the new search is active.
                        int incomingRevision=ReadInt(settingsProgram,"configRevision")+1;
                        settingsProgram.SetProgramVariable("sharedPreset",GoDifficultyProfile.MASTER);
                        settingsProgram.SetProgramVariable("configRevision",incomingRevision);
                        settingsProgram.SendCustomEvent("_onDeserialization");
                        stage=11;stageFrames=0;return;
                    }
                    FailIfTimedOut("AIvAI next real search did not start with new configuration");return;
                }
                if(stage==11)
                {
                    if(ReadInt(searchProgram,"searchToken")!=remoteBaselineToken||
                        ReadInt(searchProgram,"targetVisits")!=remoteBaselineTarget)
                    {Fail("remote configuration deserialization interrupted the active next search");return;}
                    if(ReadInt(blackProfileProgram,"maxVisits")!=GoDifficultyProfile.MASTER_VISITS||
                        ReadInt(blackProfileProgram,"maxNNQueries")!=GoDifficultyProfile.MASTER_VISITS||
                        ReadInt(blackProfileProgram,"maxTransitionsPerFrame")!=4||
                        Mathf.Abs(ReadFloat(blackProfileProgram,"cpuct")-1.35f)>0.001f||
                        Mathf.Abs(ReadFloat(blackProfileProgram,"moveTemperature")-0.10f)>0.001f||
                        ReadInt(blackProfileProgram,"policyTopK")!=128||
                        Mathf.Abs(ReadFloat(blackProfileProgram,"resignThreshold")-(-0.98f))>0.001f)
                    {Fail("remote settings snapshot did not resolve the complete Master profile");return;}
                    stage=12;stageFrames=0;return;
                }
                if(stage==12)
                {
                    int phase=ReadInt(searchProgram,"phase");
                    int token=ReadInt(searchProgram,"searchToken");
                    bool active=phase==GoMctsSearch.PHASE_WAITING_NEURAL||phase==GoMctsSearch.PHASE_EXPANDING||
                        phase==GoMctsSearch.PHASE_SELECTING||phase==GoMctsSearch.PHASE_BACKING_UP||phase==GoMctsSearch.PHASE_RUNNING;
                    if(active&&token!=remoteBaselineToken)
                    {
                        bool captured=ReadInt(searchProgram,"targetVisits")==GoDifficultyProfile.MASTER_VISITS&&
                            Mathf.Abs(ReadFloat(searchProgram,"explorationConstant")-1.35f)<0.001f&&
                            Mathf.Abs(ReadFloat(searchProgram,"rootMoveTemperature")-0.10f)<0.001f&&
                            ReadInt(searchProgram,"rootPolicyTopK")==128&&
                            ReadInt(aiProgram,"activeSearchTransitionsPerFrame")==4&&
                            Mathf.Abs(ReadFloat(aiProgram,"activeSearchResignThreshold")-(-0.98f))<0.001f;
                        if(!captured)
                        {Fail("next real search did not capture the remotely deserialized complete Master profile");return;}
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_SETTINGS_PASS applyPreservesCurrent=true nextTarget="+
                            remoteBaselineTarget+" remotePreservesCurrent=true remoteNextTarget="+
                            GoDifficultyProfile.MASTER_VISITS+" completeProfiles=true");
                        StopWithResult(true);return;
                    }
                    FailIfTimedOut("next real search did not consume remotely deserialized settings");return;
                }
            }
            catch(Exception exception){Fail("settings lifecycle exception: "+exception);}
        }

        private static void ResolvePrograms()
        {
            if(uiProxy==null)uiProxy=UnityEngine.Object.FindObjectOfType<GoUI>();
            if(uiProxy==null)return;
            if(uiProgram==null)uiProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy);
            if(uiProxy.game!=null&&gameProgram==null)gameProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.game);
            if(uiProxy.aiController!=null&&aiProgram==null)aiProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.aiController);
            if(uiProxy.aiController!=null&&uiProxy.aiController.search!=null&&searchProgram==null)
                searchProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.aiController.search);
            if(uiProxy.aiSettings!=null&&settingsProgram==null)settingsProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.aiSettings);
            if(uiProxy.blackDifficulty!=null&&blackProfileProgram==null)blackProfileProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.blackDifficulty);
            if(uiProxy.whiteDifficulty!=null&&whiteProfileProgram==null)whiteProfileProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(uiProxy.whiteDifficulty);
            blackVisitsInput=uiProxy.blackCustomVisitsInput;
            whiteVisitsInput=uiProxy.whiteCustomVisitsInput;
        }

        private static Button FindButton(int action)
        {
            if(uiProxy==null||uiProxy.controlButtons==null||uiProxy.controlButtonActions==null)return null;
            int count=Mathf.Min(uiProxy.controlButtons.Length,uiProxy.controlButtonActions.Length);
            for(int i=0;i<count;i++)if(uiProxy.controlButtonActions[i]==action)return uiProxy.controlButtons[i];
            return null;
        }

        private static void Press(Button button,string label)
        {
            if(button==null||button.onClick==null||button.onClick.GetPersistentEventCount()!=1)
                throw new InvalidOperationException("generated settings Button.onClick is incomplete: "+label);
            button.onClick.Invoke();
        }

        private static int ReadInt(UdonBehaviour program,string name)
        {
            object value=program==null?null:program.GetProgramVariable(name);
            return value==null?0:Convert.ToInt32(value);
        }

        private static float ReadFloat(UdonBehaviour program,string name)
        {
            object value=program==null?null:program.GetProgramVariable(name);
            return value==null?0f:Convert.ToSingle(value);
        }

        private static bool ReadBool(UdonBehaviour program,string name)
        {
            object value=program==null?null:program.GetProgramVariable(name);
            return value!=null&&Convert.ToBoolean(value);
        }

        private static bool MatchesProfile(UdonBehaviour program,int preset,int visits,
            int transitions,float cpuct,float temperature,int topK,float resign)
        {
            return ReadInt(program,"preset")==preset&&
                ReadInt(program,"maxVisits")==visits&&
                ReadInt(program,"maxNNQueries")==visits&&
                ReadInt(program,"maxTransitionsPerFrame")==transitions&&
                Mathf.Abs(ReadFloat(program,"cpuct")-cpuct)<0.001f&&
                Mathf.Abs(ReadFloat(program,"moveTemperature")-temperature)<0.001f&&
                ReadInt(program,"policyTopK")==topK&&
                Mathf.Abs(ReadFloat(program,"resignThreshold")-resign)<0.001f;
        }

        private static void FailIfTimedOut(string message)
        {
            if(EditorApplication.timeSinceStartup>=deadline)Fail(message);
        }

        private static void Fail(string message)
        {
            if(resultWritten)return;
            resultWritten=true;SessionState.SetBool(PendingKey,false);
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_SETTINGS_FAIL "+message);
            EditorApplication.update-=Pump;EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
            EditorApplication.Exit(1);
        }

        private static void StopWithResult(bool passed)
        {
            if(resultWritten)return;
            resultWritten=true;SessionState.SetBool(PendingKey,false);
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SETTINGS_RESULT pass="+passed);
            EditorApplication.update-=Pump;EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
            EditorApplication.Exit(passed?0:1);
        }
    }
}
#endif
