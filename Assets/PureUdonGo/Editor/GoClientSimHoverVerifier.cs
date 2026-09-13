#if UNITY_EDITOR
using System;
using System.IO;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>Single-VM hover/cache gate; desktop/VR laser behavior remains FUTURE_MANUAL.</summary>
    public static class GoClientSimHoverVerifier
    {
        private const double TimeoutSeconds=300.0;
        private const string PendingKey="PureUdonGo.ClientSimHoverVerifier.Pending";
        private static double deadline;private static bool entered;private static bool resultWritten;
        private static UdonBehaviour probeProgram;
        private static bool probeSent;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {if(!SessionState.GetBool(PendingKey,false))return;deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;EditorApplication.update+=Pump;}

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Hover And MoveMask Budget")]
        public static void VerifyHover()
        {
            ResetRunner();Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();GoGame game=UnityEngine.Object.FindObjectOfType<GoGame>();GoBoardView view=game==null?null:game.view;GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if(root==null||game==null||view==null||pool==null)throw new InvalidOperationException("Hover fixture references missing");
            string path=EnsureProbeAsset();GameObject go=new GameObject("ClientSim Hover Sweep Probe");go.transform.SetParent(root.transform,false);GoHoverSweepProbe probe=go.AddUdonSharpComponent<GoHoverSweepProbe>();probe.game=game;probe.view=view;probe.pool=pool;probe.cells=view.GetComponentsInChildren<GoBoardCell>(true);probe.inputReceiver=view.inputReceiver;probe.visibleGames=new GoGame[Mathf.Min(GoBoardPool.FIXED_VISIBLE_TABLES,pool.tableRoots.Length)];for(int i=0;i<probe.visibleGames.Length;i++)probe.visibleGames[i]=pool.tableRoots[i].GetComponentInChildren<GoGame>(true);UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions{IsEditorBuild=true,ConcurrentBuild=false,DisableLogging=false});UdonSharpEditorUtility.CopyProxyToUdon(probe,ProxySerializationPolicy.All);
            ClientSimSettings.SaveSettings(new ClientSimSettings{enableClientSim=true,displayLogs=true,deleteEditorOnly=false,spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,localPlayerIsMaster=true,isInstanceOwner=true,initializationDelay=0f,currentLanguage="en"});SessionState.SetBool(PendingKey,true);EditorSceneManager.SetActiveScene(scene);deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;EditorApplication.update+=Pump;Debug.Log("PURE_UDON_GO_CLIENTSIM_HOVER_START");EditorApplication.isPlaying=true;
        }

        private static string EnsureProbeAsset(){MonoScript source=null;foreach(string guid in AssetDatabase.FindAssets("t:MonoScript",new[]{"Assets"})){string p=AssetDatabase.GUIDToAssetPath(guid);MonoScript s=AssetDatabase.LoadAssetAtPath<MonoScript>(p);if(s!=null&&s.GetClass()==typeof(GoHoverSweepProbe)){source=s;break;}}if(source==null)throw new InvalidOperationException("Hover probe source missing");string sp=AssetDatabase.GetAssetPath(source).Replace('\\','/');string pp=Path.Combine(Path.GetDirectoryName(sp),Path.GetFileNameWithoutExtension(sp)+".asset").Replace('\\','/');UdonSharpProgramAsset a=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(pp);if(a==null){a=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();a.sourceCsScript=source;AssetDatabase.CreateAsset(a,pp);}else a.sourceCsScript=source;a.ScriptVersion=UdonSharpProgramVersion.CurrentVersion;EditorUtility.SetDirty(a);AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);return pp;}
        private static void ResetRunner(){EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;EditorApplication.update-=Pump;SessionState.SetBool(PendingKey,false);deadline=0;entered=false;resultWritten=false;probeProgram=null;probeSent=false;}
        private static void OnPlayModeStateChanged(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode)entered=true;else if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(PendingKey,false))Fail("play mode ended before hover gate");}
        private static void Pump(){if(resultWritten||!EditorApplication.isPlaying)return;if(!entered)entered=true;try{if(!ClientSimMain.HasInstance()){FailIfTimedOut("ClientSim did not initialize");return;}if(probeProgram==null){GoHoverSweepProbe p=UnityEngine.Object.FindObjectOfType<GoHoverSweepProbe>();if(p!=null)probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(p);}if(probeProgram==null){FailIfTimedOut("hover probe program missing");return;}if(!probeSent){probeProgram.SendCustomEvent("RunHoverSweepProbe");probeSent=true;return;}if(!ReadBool("probeFinished")){FailIfTimedOut("hover sweep did not complete");return;}if(!ReadBool("probePassed")){Fail("hover gate failed: "+ReadString("failure"));return;}Debug.Log("PURE_UDON_GO_CLIENTSIM_HOVER_PASS warmupPoints="+ReadInt("warmupPoints")+" warmupFrames="+ReadInt("warmupFrames")+" fullRefreshDelta="+(ReadInt("fullRefreshAfterSweep")-ReadInt("fullRefreshBeforeSweep"))+" poolMaxPoints="+ReadInt("poolMaxPointsOneFrame"));Stop(true);}catch(Exception e){Fail("hover inspection exception: "+e);}}
        private static int ReadInt(string n){object v=probeProgram.GetProgramVariable(n);return v==null?0:Convert.ToInt32(v);}private static bool ReadBool(string n){object v=probeProgram.GetProgramVariable(n);return v!=null&&Convert.ToBoolean(v);}private static string ReadString(string n){object v=probeProgram.GetProgramVariable(n);return v==null?"":v.ToString();}private static void FailIfTimedOut(string m){if(EditorApplication.timeSinceStartup>=deadline)Fail(m);}private static void Fail(string m){if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_HOVER_FAIL "+m);Stop(false);}private static void Stop(bool pass){resultWritten=true;SessionState.SetBool(PendingKey,false);EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;EditorApplication.update-=Pump;Debug.Log("PURE_UDON_GO_CLIENTSIM_HOVER_RESULT pass="+pass);EditorApplication.Exit(pass?0:1);}
    }
}
#endif
