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
    /// <summary>
    /// Runs the fixed A-H corpus in one ClientSim VM. This is a telemetry gate,
    /// not a clean VRChat-client frame-time claim; every sample still executes
    /// the production rules, V7 encoder, GPU graph, readback and PUCT path.
    /// </summary>
    public static class GoClientSimPerformanceCorpusVerifier
    {
        private const double TimeoutSeconds=1200.0;
        private const string PendingKey="PureUdonGo.ClientSimPerformanceCorpus.Pending";
        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool probeSent;
        private static UdonBehaviour probeProgram;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if(!SessionState.GetBool(PendingKey,false))return;
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim A-H Performance Corpus")]
        public static void VerifyPerformanceCorpus()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoGame game=UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller=UnityEngine.Object.FindObjectOfType<GoAiController>();
            GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if(root==null||game==null||controller==null||pool==null)
                throw new InvalidOperationException("Generated runtime references were not found");
            string programPath=EnsureProbeProgramAsset();
            GameObject go=new GameObject("ClientSim A-H Performance Corpus Probe");
            go.transform.SetParent(root.transform,false);
            GoPerformanceCorpusProbe probe=go.AddUdonSharpComponent<GoPerformanceCorpusProbe>();
            probe.game=game;probe.controller=controller;probe.aiSettings=game.aiSettings;
            probe.boardPool=pool;probe.encoder=controller.encoder;
            CompileAndCopyProbe(probe,programPath);
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_CORPUS_START fixtures=A-H diagnosticBudgets="+
                GoPerformanceCorpusProbe.BEGINNER_TEST_VISITS+","+
                GoPerformanceCorpusProbe.ADVANCED_TEST_VISITS+" timeoutSeconds="+TimeoutSeconds);
            EditorApplication.isPlaying=true;
        }

        private static string EnsureProbeProgramAsset()
        {
            MonoScript source=null;string[] guids=AssetDatabase.FindAssets("t:MonoScript",new[]{"Assets"});
            for(int i=0;i<guids.Length;i++)
            {
                string path=AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript candidate=AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if(candidate!=null&&candidate.GetClass()==typeof(GoPerformanceCorpusProbe)){source=candidate;break;}
            }
            if(source==null)throw new InvalidOperationException("A-H probe source is missing");
            string sourcePath=AssetDatabase.GetAssetPath(source).Replace('\\','/');
            string programPath=Path.Combine(Path.GetDirectoryName(sourcePath),Path.GetFileNameWithoutExtension(sourcePath)+".asset").Replace('\\','/');
            UdonSharpProgramAsset asset=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
            if(asset==null){asset=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();asset.sourceCsScript=source;AssetDatabase.CreateAsset(asset,programPath);}
            else asset.sourceCsScript=source;
            asset.ScriptVersion=UdonSharpProgramVersion.CurrentVersion;EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return programPath;
        }

        private static void CompileAndCopyProbe(GoPerformanceCorpusProbe probe,string path)
        {
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions{IsEditorBuild=true,ConcurrentBuild=false,DisableLogging=false});
            if(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path)==null)
                throw new InvalidOperationException("A-H probe program asset did not compile");
            UdonSharpEditorUtility.CopyProxyToUdon(probe,ProxySerializationPolicy.All);
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;EditorApplication.update-=Pump;
            SessionState.SetBool(PendingKey,false);deadline=0;enteredPlayMode=false;resultWritten=false;probeProgram=null;
            probeSent=false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)enteredPlayMode=true;
            else if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(PendingKey,false))Fail("play mode ended before A-H corpus finished");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance()){FailIfTimedOut("ClientSim did not initialize");return;}
                if(probeProgram==null)
                {
                    GoPerformanceCorpusProbe probe=UnityEngine.Object.FindObjectOfType<GoPerformanceCorpusProbe>();
                    if(probe!=null)probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if(probeProgram==null){FailIfTimedOut("A-H probe program was not found");return;}
                if(!probeSent)
                {
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_CORPUS_EVENT name=RunPerformanceCorpusProbe");
                    probeProgram.SendCustomEvent("RunPerformanceCorpusProbe");
                    probeSent=true;
                    return;
                }
                if(!ReadBool(probeProgram,"probeFinished"))return;
                if(!ReadBool(probeProgram,"probePassed")){Fail("A-H corpus failed: "+ReadString(probeProgram,"failure"));return;}
                int[] actual=probeProgram.GetProgramVariable("actualVisits") as int[];
                int[] frames=probeProgram.GetProgramVariable("searchFrames") as int[];
                int[] fixtureHashes=probeProgram.GetProgramVariable("fixtureSequenceHashes") as int[];
                int[] sampleCounts=probeProgram.GetProgramVariable("frameSampleCounts") as int[];
                float[] sampleValues=probeProgram.GetProgramVariable("frameSamples") as float[];
                float[] p50=new float[GoPerformanceCorpusProbe.SAMPLE_COUNT];
                float[] p95=new float[GoPerformanceCorpusProbe.SAMPLE_COUNT];
                float[] p99=new float[GoPerformanceCorpusProbe.SAMPLE_COUNT];
                float[] max=probeProgram.GetProgramVariable("frameMaxMilliseconds") as float[];
                ComputeFramePercentiles(sampleCounts,sampleValues,p50,p95,p99);
                Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_CORPUS_PASS samples=16 diagnosticVisits="+
                    GoPerformanceCorpusProbe.BEGINNER_TEST_VISITS+","+
                    GoPerformanceCorpusProbe.ADVANCED_TEST_VISITS+" actual="+
                    DescribeIntArray(actual)+" frames="+DescribeIntArray(frames)+
                    " fixtureHashes="+DescribeIntArray(fixtureHashes)+
                    " p50="+DescribeFloatArray(p50)+" p95="+DescribeFloatArray(p95)+
                    " p99="+DescribeFloatArray(p99)+" max="+DescribeFloatArray(max));
                StopWithResult(true);
            }
            catch(Exception exception){Fail("A-H corpus inspection exception: "+exception);}
        }

        private static string DescribeIntArray(int[] values)
        {if(values==null)return "null";string s="[";for(int i=0;i<values.Length;i++){if(i>0)s+=",";s+=values[i];}return s+"]";}
        private static string DescribeFloatArray(float[] values)
        {if(values==null)return "null";string s="[";for(int i=0;i<values.Length;i++){if(i>0)s+=",";s+=values[i].ToString("F2");}return s+"]";}

        private static void ComputeFramePercentiles(int[] counts,float[] samples,
            float[] p50,float[] p95,float[] p99)
        {
            if(counts==null||samples==null)return;
            int capacity=GoPerformanceCorpusProbe.FRAME_SAMPLE_CAPACITY;
            for(int sample=0;sample<GoPerformanceCorpusProbe.SAMPLE_COUNT;sample++)
            {
                int count=Mathf.Clamp(counts[sample],0,capacity);
                if(count<=0)continue;
                float[] sorted=new float[count];
                Array.Copy(samples,sample*capacity,sorted,0,count);
                Array.Sort(sorted);
                p50[sample]=Percentile(sorted,0.50f);
                p95[sample]=Percentile(sorted,0.95f);
                p99[sample]=Percentile(sorted,0.99f);
            }
        }

        private static float Percentile(float[] sorted,float fraction)
        {
            if(sorted==null||sorted.Length==0)return 0f;
            int index=Mathf.Clamp(Mathf.CeilToInt(sorted.Length*fraction)-1,
                0,sorted.Length-1);
            return sorted[index];
        }
        private static bool ReadBool(UdonBehaviour p,string n){object v=p==null?null:p.GetProgramVariable(n);return v!=null&&Convert.ToBoolean(v);}
        private static string ReadString(UdonBehaviour p,string n){object v=p==null?null:p.GetProgramVariable(n);return v==null?"":v.ToString();}
        private static void FailIfTimedOut(string m){if(EditorApplication.timeSinceStartup>=deadline)Fail(m);}
        private static void Fail(string m){if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_PERF_CORPUS_FAIL "+m);StopWithResult(false);}
        private static void StopWithResult(bool pass){resultWritten=true;SessionState.SetBool(PendingKey,false);EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;EditorApplication.update-=Pump;Debug.Log("PURE_UDON_GO_CLIENTSIM_PERF_CORPUS_RESULT pass="+pass);EditorApplication.Exit(pass?0:1);}
    }
}
#endif
