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
    public static class GoClientSimGpuGraphVerifier
    {
        private const double TimeoutSeconds=180.0;
        private const string PendingSessionKey="PureUdonGo.ClientSimGpuGraphVerifier.Pending";
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_GPU_GRAPH_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim GPU Graph Slicing")]
        public static void VerifyClientSimGpuGraphSlicing()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,
                OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoGame game=pool!=null&&pool.tableRoots!=null&&pool.tableRoots.Length>0&&
                pool.tableRoots[0]!=null?pool.tableRoots[0].GetComponentInChildren<GoGame>(true):
                UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller=game==null?null:game.GetComponentInChildren<GoAiController>(true);
            if(root==null||game==null||controller==null||controller.runtime==null)
                throw new InvalidOperationException("Generated GPU graph runtime was not found");
            string asset=GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoGpuGraphSlicingProbe),"GPU-graph-slicing");
            GameObject probeObject=new GameObject("ClientSim GPU Graph Slicing Probe");
            probeObject.transform.SetParent(root.transform,false);
            GoGpuGraphSlicingProbe probe=probeObject.AddUdonSharpComponent<GoGpuGraphSlicingProbe>();
            probe.game=game;probe.controller=controller;probe.runtime=controller.runtime;
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_GPU_GRAPH_START timeoutSeconds="+TimeoutSeconds);
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
                Fail("PlayMode ended before GPU graph probe completed");
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
                    GoGpuGraphSlicingProbe probe=
                        UnityEngine.Object.FindObjectOfType<GoGpuGraphSlicingProbe>();
                    if(probe!=null)probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if(probeProgram==null)
                {FailIfTimedOut("temporary Udon GPU graph probe was not found");return;}
                if(!probeSent)
                {probeProgram.SendCustomEvent("RunGpuGraphSlicingProbe");probeSent=true;return;}
                if(!ReadBool("probeFinished"))
                {FailIfTimedOut("GPU graph probe did not finish phase="+ReadInt("phase"));return;}
                bool passed=ReadBool("probePassed");
                string metrics="frames="+ReadInt("firstGraphFrames")+
                    " passes="+ReadInt("firstGraphPasses")+
                    " maxPassesOneFrame="+ReadInt("firstGraphMaxPassesOneFrame")+
                    " stageMask="+ReadInt("firstGraphStageMask")+
                     " maxSubmitMs="+ReadFloat("firstGraphMaxSubmitMilliseconds")+
                     " smoothedSubmitMs="+ReadFloat("firstGraphSmoothedSubmitMilliseconds")+
                     " secondGraphEmaMs="+ReadFloat("secondGraphSmoothedSubmitMilliseconds")+
                    " cancelledStage="+ReadInt("cancelledGraphStage")+
                    " cancelledProgress="+ReadInt("cancelledGraphProgress")+
                    " cancelledPasses="+ReadInt("cancelledGraphPasses")+
                    " passesAfterCancel="+ReadInt("passesAfterCancellation")+
                    " revision="+ReadInt("revisionBeforeCancellation")+"->"+
                    ReadInt("revisionAfterCancellation")+" failure="+ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_GPU_GRAPH_RESULT pass="+passed+" "+metrics);
                if(!passed){Fail(metrics);return;}
                Debug.Log("PURE_UDON_GO_CLIENTSIM_GPU_GRAPH_PASS "+metrics);
                StopWithResult(true);
            }
            catch(Exception exception){Fail("GPU graph inspection exception: "+exception);}
        }

        private static int ReadInt(string name)
        {object value=probeProgram.GetProgramVariable(name);return value==null?0:Convert.ToInt32(value);}
        private static float ReadFloat(string name)
        {object value=probeProgram.GetProgramVariable(name);return value==null?0f:Convert.ToSingle(value);}
        private static bool ReadBool(string name)
        {object value=probeProgram.GetProgramVariable(name);return value!=null&&Convert.ToBoolean(value);}
        private static string ReadString(string name)
        {object value=probeProgram.GetProgramVariable(name);return value==null?"":value.ToString();}
        private static void FailIfTimedOut(string message)
        {if(EditorApplication.timeSinceStartup>=deadline)Fail(message);}
        private static void Fail(string message)
        {if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_GPU_GRAPH_FAIL "+message);StopWithResult(false);}
        private static void StopWithResult(bool pass)
        {
            resultWritten=true;SessionState.SetBool(PendingSessionKey,false);
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_GPU_GRAPH_FINAL pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
