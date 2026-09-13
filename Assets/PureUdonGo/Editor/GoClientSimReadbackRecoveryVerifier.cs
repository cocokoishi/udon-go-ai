#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Adversarial A/B/C recovery sequence over real pending ClientSim GPU
    /// requests. It proves stable reader identity, both A and B late-callback
    /// recovery, reuse of each recovered channel, and final fresh search
    /// completion without accepting a stale result.
    /// </summary>
    public static class GoClientSimReadbackRecoveryVerifier
    {
        private const double TimeoutSeconds=300.0;
        private const string PendingSessionKey=
            "PureUdonGo.ClientSimReadbackRecoveryVerifier.Pending";
        private const string StageSessionKey=
            "PureUdonGo.ClientSimReadbackRecoveryVerifier.Stage";
        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static int stage;
        private static int baselineMoveCount;
        private static int baselineAiMoves;
        private static int initialSearchToken;
        private static UdonBehaviour gameProgram;
        private static UdonBehaviour aiProgram;
        private static UdonBehaviour searchProgram;
        private static UdonBehaviour readerAProgram;
        private static UdonBehaviour readerBProgram;
        private static UdonBehaviour readerCProgram;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if(!SessionState.GetBool(PendingSessionKey,false))return;
            stage=SessionState.GetInt(StageSessionKey,0);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READBACK_RECOVERY_REATTACHED stage="+stage);
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Readback Recovery Pool")]
        public static void VerifyClientSimReadbackRecoveryPool()
        {
            ResetRunner();
            Editor.GoWorldGenerator.GenerateReaderRecoveryFixtureScene();
            Scene sourceScene=EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ReaderRecoveryScenePath,
                OpenSceneMode.Single);
            if(!sourceScene.IsValid()||!sourceScene.isLoaded)
                throw new InvalidOperationException("Reader recovery fixture was not loaded");
            GoGame fixtureGame=UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController fixtureController=
                UnityEngine.Object.FindObjectOfType<GoAiController>();
            if(fixtureGame==null||fixtureController==null||
                UnityEngine.Object.FindObjectsOfType<GoGpuNeuralOutputReader>().Length!=3||
                UnityEngine.Object.FindObjectOfType<GoBoardPool>()!=null||
                UnityEngine.Object.FindObjectOfType<GoBoardCell>()!=null||
                UnityEngine.Object.FindObjectOfType<Canvas>()!=null)
                throw new InvalidOperationException("Minimal reader fixture runtime is incomplete");
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,displayLogs=true,deleteEditorOnly=false,
                spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,
                localPlayerIsMaster=true,isInstanceOwner=true,
                initializationDelay=0f,currentLanguage="en"
            });
            SessionState.SetBool(PendingSessionKey,true);
            SessionState.SetInt(StageSessionKey,0);
            EditorSceneManager.SetActiveScene(sourceScene);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READBACK_RECOVERY_START scene="+
                Editor.GoWorldGenerator.ReaderRecoveryScenePath+" timeoutSeconds="+
                TimeoutSeconds+" games=1 readers=3");
            EditorApplication.isPlaying=true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;deadline=0.0;enteredPlayMode=false;
            resultWritten=false;stage=0;baselineMoveCount=0;baselineAiMoves=0;
            initialSearchToken=0;gameProgram=null;aiProgram=null;searchProgram=null;
            readerAProgram=null;readerBProgram=null;readerCProgram=null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)enteredPlayMode=true;
            else if(state==PlayModeStateChange.EnteredEditMode&&
                SessionState.GetBool(PendingSessionKey,false))
                Fail("PlayMode ended before readback recovery completed");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance())
                {FailIfTimedOut("ClientSim did not initialize");return;}
                ResolvePrograms();
                if(gameProgram==null||aiProgram==null||searchProgram==null||
                    readerAProgram==null||readerBProgram==null||readerCProgram==null)
                {FailIfTimedOut("stable A/B/C reader programs were not found");return;}
                if(stage==0)
                {
                    baselineMoveCount=ReadInt(gameProgram,"moveCount");
                    baselineAiMoves=ReadInt(aiProgram,"aiMoves");
                    gameProgram.SendCustomEvent("SetWhiteStarts");
                    gameProgram.SendCustomEvent("RequestStartMatch");
                    SetStage(1);return;
                }
                if(stage==1)
                {
                    if(!IsWaiting(readerAProgram))
                    {FailIfTimedOut("reader A did not receive the first real request");return;}
                    initialSearchToken=ReadInt(searchProgram,"searchToken");
                    aiProgram.SendCustomEvent("InvalidateSearch");
                    SetStage(2);return;
                }
                if(stage==2)
                {
                    if(!IsWaiting(readerBProgram))
                    {FailIfTimedOut("reader A cancellation did not rotate to B");return;}
                    int aState=ReadInt(readerAProgram,"readbackState");
                    if(aState==GoGpuNeuralOutputReader.READBACK_RECOVERED)
                    {SetStage(3);return;}
                    if(!IsQuarantined(readerAProgram))
                    {FailIfTimedOut("reader A was neither quarantined nor recovered");return;}
                    readerAProgram.SendCustomEvent("ObserveDiscardedCallbackForVerifier");
                    SetStage(3);return;
                }
                if(stage==3)
                {
                    if(ReadInt(readerAProgram,"readbackState")!=
                        GoGpuNeuralOutputReader.READBACK_RECOVERED)
                    {FailIfTimedOut("reader A late callback did not recover its channel");return;}
                    aiProgram.SendCustomEvent("InvalidateSearch");
                    SetStage(4);return;
                }
                if(stage==4)
                {
                    if(!IsWaiting(readerCProgram))
                    {FailIfTimedOut("reader B cancellation did not rotate to C");return;}
                    int bState=ReadInt(readerBProgram,"readbackState");
                    if(bState==GoGpuNeuralOutputReader.READBACK_RECOVERED)
                    {SetStage(5);return;}
                    if(!IsQuarantined(readerBProgram))
                    {FailIfTimedOut("reader B was neither quarantined nor recovered");return;}
                    readerBProgram.SendCustomEvent("ObserveDiscardedCallbackForVerifier");
                    SetStage(5);return;
                }
                if(stage==5)
                {
                    if(ReadInt(readerBProgram,"readbackState")!=
                        GoGpuNeuralOutputReader.READBACK_RECOVERED)
                    {FailIfTimedOut("reader B late callback did not recover its channel");return;}
                    aiProgram.SendCustomEvent("InvalidateSearch");
                    SetStage(6);return;
                }
                if(stage==6)
                {
                    if(!IsWaiting(readerAProgram)||ReadInt(aiProgram,"activeReaderIndex")!=0||
                        ReadInt(aiProgram,"recoveredReaderSelections")<1)
                    {FailIfTimedOut("recovered reader A was not reused after C cancellation");return;}
                    aiProgram.SendCustomEvent("InvalidateSearch");
                    SetStage(7);return;
                }
                if(stage==7)
                {
                    if(!IsWaiting(readerBProgram)||ReadInt(aiProgram,"activeReaderIndex")!=1||
                        ReadInt(aiProgram,"recoveredReaderSelections")<2)
                    {FailIfTimedOut("recovered reader B was not reused after A cancellation");return;}
                    SetStage(8);return;
                }
                int controllerState=ReadInt(aiProgram,"controllerState");
                if(controllerState==GoAiController.STATE_ERROR)
                {Fail("fresh reader B search failed: "+ReadString(aiProgram,"lastError"));return;}
                int moves=ReadInt(aiProgram,"aiMoves");
                int moveCount=ReadInt(gameProgram,"moveCount");
                int visits=ReadInt(searchProgram,"visitsCompleted");
                if(moves>baselineAiMoves&&moveCount>baselineMoveCount&&visits>=GoDifficultyProfile.BEGINNER_VISITS)
                {
                    int finalToken=ReadInt(searchProgram,"searchToken");
                    bool staleSafe=finalToken>initialSearchToken&&
                        ReadInt(readerAProgram,"staleCallbacks")>=1&&
                        ReadInt(readerBProgram,"staleCallbacks")>=1;
                    if(!staleSafe)
                    {Fail("late callback identity was not discarded safely");return;}
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_READBACK_RECOVERY_PASS visits="+
                        visits+" token="+initialSearchToken+"->"+finalToken+
                        " recoveredSelections="+
                        ReadInt(aiProgram,"recoveredReaderSelections")+
                        " states="+ReadInt(readerAProgram,"readbackState")+","+
                        ReadInt(readerBProgram,"readbackState")+","+
                        ReadInt(readerCProgram,"readbackState")+
                        " stale="+ReadInt(readerAProgram,"staleCallbacks")+","+
                        ReadInt(readerBProgram,"staleCallbacks")+","+
                        ReadInt(readerCProgram,"staleCallbacks"));
                    StopWithResult(true);return;
                }
                FailIfTimedOut("recovered reader B did not complete a fresh move");
            }
            catch(Exception exception){Fail("readback recovery inspection exception: "+exception);}
        }

        private static void ResolvePrograms()
        {
            GoAiController ai=UnityEngine.Object.FindObjectOfType<GoAiController>();
            if(ai==null)return;
            if(aiProgram==null)aiProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai);
            if(gameProgram==null&&ai.game!=null)
                gameProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai.game);
            if(searchProgram==null&&ai.search!=null)
                searchProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai.search);
            if(readerAProgram==null&&ai.readerA!=null)
                readerAProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai.readerA);
            if(readerBProgram==null&&ai.readerB!=null)
                readerBProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai.readerB);
            if(readerCProgram==null&&ai.readerC!=null)
                readerCProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai.readerC);
        }

        private static bool IsWaiting(UdonBehaviour reader)
        {return ReadInt(reader,"readbackState")==GoGpuNeuralOutputReader.READBACK_WAITING;}
        private static bool IsQuarantined(UdonBehaviour reader)
        {return ReadInt(reader,"readbackState")==GoGpuNeuralOutputReader.READBACK_QUARANTINED&&ReadBool(reader,"quarantinePending");}
        private static int ReadInt(UdonBehaviour program,string name)
        {object value=program.GetProgramVariable(name);return value==null?0:Convert.ToInt32(value);}
        private static bool ReadBool(UdonBehaviour program,string name)
        {object value=program.GetProgramVariable(name);return value!=null&&Convert.ToBoolean(value);}
        private static string ReadString(UdonBehaviour program,string name)
        {object value=program.GetProgramVariable(name);return value==null?"":value.ToString();}
        private static void SetStage(int value)
        {stage=value;SessionState.SetInt(StageSessionKey,value);}
        private static void FailIfTimedOut(string message)
        {if(EditorApplication.timeSinceStartup>=deadline)Fail(message+" stage="+stage);}
        private static void Fail(string message)
        {if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_READBACK_RECOVERY_FAIL "+message);StopWithResult(false);}
        private static void StopWithResult(bool pass)
        {
            resultWritten=true;SessionState.SetBool(PendingSessionKey,false);
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_READBACK_RECOVERY_RESULT pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
