#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    public static class GoClientSimDerivedHistoryVerifier
    {
        private const double TimeoutSeconds=240.0;
        private const string PendingSessionKey=
            "PureUdonGo.ClientSimDerivedHistoryVerifier.Pending";
        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if(!SessionState.GetBool(PendingSessionKey,false))return;
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_DERIVED_HISTORY_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Derived History Rebuild")]
        public static void VerifyClientSimDerivedHistoryRebuild()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,
                OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if(root==null||pool==null||pool.tableRoots==null||pool.tableRoots.Length<2)
                throw new InvalidOperationException("Two generated table slots are required");
            GoGame source=pool.tableRoots[0].GetComponentInChildren<GoGame>(true);
            GoGame replica=pool.tableRoots[1].GetComponentInChildren<GoGame>(true);
            GoAiController sourceController=pool.tableRoots[0].GetComponentInChildren<GoAiController>(true);
            GoAiController replicaController=pool.tableRoots[1].GetComponentInChildren<GoAiController>(true);
            if(source==null||replica==null||sourceController==null||replicaController==null||
                sourceController.encoder==null||replicaController.encoder==null)
                throw new InvalidOperationException("Derived-history runtime references are incomplete");
            string asset=GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoDerivedHistoryRebuildProbe),"Derived-history-rebuild");
            GameObject probeObject=new GameObject("ClientSim Derived History Rebuild Probe");
            probeObject.transform.SetParent(root.transform,false);
            GoDerivedHistoryRebuildProbe probe=
                probeObject.AddUdonSharpComponent<GoDerivedHistoryRebuildProbe>();
            probe.source=source;probe.replica=replica;
            probe.sourceController=sourceController;probe.replicaController=replicaController;
            probe.sourceEncoder=sourceController.encoder;probe.replicaEncoder=replicaController.encoder;
            probe.boardPool=pool;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe,asset);
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,displayLogs=true,deleteEditorOnly=false,
                spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,
                localPlayerIsMaster=true,isInstanceOwner=true,
                initializationDelay=0f,currentLanguage="en"
            });
            SessionState.SetBool(PendingSessionKey,true);
            EditorSceneManager.SetActiveScene(scene);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_DERIVED_HISTORY_START historyMoves=160");
            EditorApplication.isPlaying=true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;deadline=0.0;enteredPlayMode=false;
            resultWritten=false;probeSent=false;probeProgram=null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)enteredPlayMode=true;
            else if(state==PlayModeStateChange.EnteredEditMode&&
                SessionState.GetBool(PendingSessionKey,false))
                Fail("PlayMode ended before derived-history probe completed");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance())
                {FailIfTimedOut("ClientSim did not initialize");return;}
                if(probeProgram==null)
                {
                    GoDerivedHistoryRebuildProbe probe=
                        UnityEngine.Object.FindObjectOfType<GoDerivedHistoryRebuildProbe>();
                    if(probe!=null)probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if(probeProgram==null)
                {FailIfTimedOut("temporary Udon derived-history probe was not found");return;}
                if(!probeSent)
                {probeProgram.SendCustomEvent("RunDerivedHistoryRebuildProbe");probeSent=true;return;}
                if(!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("derived-history probe did not finish phase="+
                        ReadInt("phase")+" moves="+ReadInt("moveIndex")+
                        " cursor="+ReadInt("compareCursor"));
                    return;
                }
                bool passed=ReadBool("probePassed");
                    string metrics="rebuild="+ReadBool("rebuildSucceeded")+
                    " transactionalCorruption="+ReadBool("transactionalCorruptionPassed")+
                    " moves="+ReadInt("moveIndex")+
                    " hashMismatch="+ReadInt("hashMismatches")+
                    " stoneCountMismatch="+ReadInt("stoneCountMismatches")+
                    " legalMismatch="+ReadInt("legalMaskMismatches")+
                    " superkoMismatch="+ReadInt("superkoMaskMismatches")+
                    " featureMismatch="+ReadInt("featureMismatches")+
                    " estimatedGoGameIntArrayBytes="+ReadInt("estimatedGoGameIntArrayBytes")+
                    " legacyRawSyncedBytes="+ReadInt("legacyRawSyncedBytes")+
                    " failure="+ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_DERIVED_HISTORY_RESULT pass="+
                    passed+" "+metrics);
                if(!passed){Fail(metrics);return;}
                Debug.Log("PURE_UDON_GO_CLIENTSIM_DERIVED_HISTORY_PASS "+metrics);
                StopWithResult(true);
            }
            catch(Exception exception){Fail("derived-history inspection exception: "+exception);}
        }

        private static int ReadInt(string name)
        {object value=probeProgram.GetProgramVariable(name);return value==null?0:Convert.ToInt32(value);}
        private static bool ReadBool(string name)
        {object value=probeProgram.GetProgramVariable(name);return value!=null&&Convert.ToBoolean(value);}
        private static string ReadString(string name)
        {object value=probeProgram.GetProgramVariable(name);return value==null?"":value.ToString();}
        private static void FailIfTimedOut(string message)
        {if(EditorApplication.timeSinceStartup>=deadline)Fail(message);}
        private static void Fail(string message)
        {if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_DERIVED_HISTORY_FAIL "+message);StopWithResult(false);}
        private static void StopWithResult(bool pass)
        {
            resultWritten=true;SessionState.SetBool(PendingSessionKey,false);
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_DERIVED_HISTORY_FINAL pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
