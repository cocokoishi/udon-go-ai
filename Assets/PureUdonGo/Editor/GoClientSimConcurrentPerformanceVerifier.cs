#if UNITY_EDITOR
using System;
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
    /// <summary>Exact default-profile frame-tail profiler for two or three tables.</summary>
    public static class GoClientSimConcurrentPerformanceVerifier
    {
        private const double TimeoutSeconds=300.0;
        private const string PendingKey="PureUdonGo.ConcurrentPerf.Pending";
        private const string CountKey="PureUdonGo.ConcurrentPerf.Count";
        private static int tableCount=2;
        private static double deadline;
        private static bool enteredPlayMode,resultWritten,started;
        private static GoBoardPool pool;
        private static UdonBehaviour poolProgram;
        private static UdonBehaviour[] games;
        private static UdonBehaviour[] controllers;
        private static int[] baselineMoves;
        private static int[] baselineAiMoves;
        private static int[] lastObservedActiveFrame;
        private static int[] schedulerGrantCounts;
        private static int[] observedSchedulerSlots;
        private static int[] observedSchedulerStrides;
        private static int[] completedVisits;
        private static float[] completionWallMilliseconds;
        private static float[] maxLiveGpuSubmitMilliseconds;
        private static bool[] tableCompleted;
        private static double searchesStartedAt;
        private static int observedActiveFrames;
        private static int maxObservedDispatches;
        private static int maxObservedRunningSearches;
        private static int maxSchedulerGap;
        private static bool schedulerObservationFailed;

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if(!SessionState.GetBool(PendingKey,false))return;
            tableCount=SessionState.GetInt(CountKey,2);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged-=OnPlayMode;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
        }

[MenuItem("Tools/Pure Udon Go/Tests/Profile ClientSim 2 Concurrent Tables")]
        public static void ProfileTwoTables(){Begin(2);}

[MenuItem("Tools/Pure Udon Go/Tests/Profile ClientSim 1 Concurrent Table")]
        public static void ProfileOneTable(){Begin(1);}

[MenuItem("Tools/Pure Udon Go/Tests/Profile ClientSim 3 Concurrent Tables")]
        public static void ProfileThreeTables(){Begin(3);}

        private static void Begin(int count)
        {
            Reset();tableCount=count;
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,
                OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded");
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,displayLogs=true,deleteEditorOnly=false,
                spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,
                localPlayerIsMaster=true,isInstanceOwner=true,
                initializationDelay=0f,currentLanguage="en"
            });
            SessionState.SetBool(PendingKey,true);SessionState.SetInt(CountKey,count);
            EditorSceneManager.SetActiveScene(scene);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_CONCURRENT_PERF_START tables="+count+
                " visits="+GoDifficultyProfile.BEGINNER_VISITS+" timeoutSeconds="+TimeoutSeconds);
            EditorApplication.isPlaying=true;
        }

        private static void Reset()
        {
            EditorApplication.playModeStateChanged-=OnPlayMode;
            EditorApplication.update-=Pump;deadline=0;enteredPlayMode=false;
            resultWritten=false;started=false;pool=null;poolProgram=null;
            games=null;controllers=null;baselineMoves=null;baselineAiMoves=null;
            lastObservedActiveFrame=null;schedulerGrantCounts=null;
            observedSchedulerSlots=null;observedSchedulerStrides=null;
            completedVisits=null;completionWallMilliseconds=null;
            maxLiveGpuSubmitMilliseconds=null;tableCompleted=null;
            searchesStartedAt=0.0;
            observedActiveFrames=0;maxObservedDispatches=0;maxSchedulerGap=0;
            maxObservedRunningSearches=0;
            schedulerObservationFailed=false;
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)enteredPlayMode=true;
            else if(state==PlayModeStateChange.EnteredEditMode&&
                SessionState.GetBool(PendingKey,false))Fail("PlayMode ended early");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance()){Timeout("ClientSim did not initialize");return;}
                if(pool==null)
                {
                    pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
                    if(pool!=null)poolProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(pool);
                }
                if(pool==null||pool.tableRoots==null||pool.tableRoots.Length<tableCount)
                {Timeout("generated board pool was not found");return;}
                if(!started){StartSearches();return;}
                ObserveSchedulerFrame();
                bool complete=true;
                for(int i=0;i<tableCount;i++)
                {
                    if(ReadInt(controllers[i],"controllerState")==GoAiController.STATE_ERROR)
                    {Fail("table "+i+" AI error: "+ReadString(controllers[i],"lastError"));return;}
                    if(!tableCompleted[i]&&
                        ReadInt(games[i],"moveCount")>baselineMoves[i]&&
                        ReadInt(controllers[i],"aiMoves")>baselineAiMoves[i])
                    {
                        int visits=ReadInt(controllers[i],"lastSearchCompletedVisits");
                        if(visits!=GoDifficultyProfile.BEGINNER_VISITS)
                        {Fail("table "+i+" completed with "+visits+" visits");return;}
                        tableCompleted[i]=true;completedVisits[i]=visits;
                        completionWallMilliseconds[i]=(float)
                            ((EditorApplication.timeSinceStartup-searchesStartedAt)*1000.0);
                        // Freeze this table at its first completed move so an
                        // immediately-started next AIvAI turn cannot overwrite
                        // the measurement before the other tables finish.
                        controllers[i].SetProgramVariable("autoStart",false);
                    }
                    if(!tableCompleted[i])complete=false;
                }
                if(!complete){Timeout("concurrent searches did not all finish");return;}
                poolProgram.SendCustomEvent("RefreshMemoryAccounting");
                if(maxObservedDispatches<1||maxObservedDispatches>tableCount)
                {Fail("pool dispatch capacity was outside the active-table bound: "+
                    maxObservedDispatches);return;}
                if(observedActiveFrames<=0||schedulerObservationFailed)
                {Fail("concurrent searches did not produce a valid active scheduler observation");return;}
                for(int i=0;i<tableCount;i++)
                    if(schedulerGrantCounts==null||schedulerGrantCounts[i]<=0)
                    {Fail("table "+i+" received no observed scheduler grant");return;}
                if(ReadInt(poolProgram,"maxMoveMaskWarmupPointsOneFrame")>
                    ReadInt(poolProgram,"moveMaskWarmupQuota"))
                {Fail("room move-mask warm-up exceeded its configured quota");return;}
                string stageMetrics="";
                for(int i=0;i<tableCount;i++)
                    stageMetrics+=" table"+i+"TickMaxMs="+
                        ReadFloat(controllers[i],"maxTickMilliseconds").ToString("F3")+
                        " table"+i+"SuperkoMaxMs="+
                        ReadFloat(controllers[i],"maxSuperkoFrameMilliseconds").ToString("F3")+
                        " table"+i+"MctsMaxMs="+
                        ReadFloat(controllers[i],"maxMctsStepMilliseconds").ToString("F3")+
                        " table"+i+"GpuMaxMs="+
                        ReadFloat(ReadReference(controllers[i],"runtime"),
                            "maxGpuSubmitMsOneFrame").ToString("F3")+
                        " table"+i+"GpuSmoothMs="+
                        maxLiveGpuSubmitMilliseconds[i].ToString("F3")+
                        " table"+i+"Slot="+observedSchedulerSlots[i]+
                        " table"+i+"Stride="+observedSchedulerStrides[i]+
                        " table"+i+"WallMs="+
                            completionWallMilliseconds[i].ToString("F3")+
                        " table"+i+"CompletedVisits="+completedVisits[i]+
                        " table"+i+"SchedulerGrants="+schedulerGrantCounts[i];
                Debug.Log("PURE_UDON_GO_CLIENTSIM_CONCURRENT_PERF_PASS tables="+
                    tableCount+" visitsEach="+GoDifficultyProfile.BEGINNER_VISITS+" "+FrameTail()+
                    " maxRunningSearches="+maxObservedRunningSearches+
                    " maxDispatchesPerFrame="+maxObservedDispatches+
                    " observedActiveFrames="+observedActiveFrames+
                    " maxObservedDispatches="+maxObservedDispatches+
                    " maxSchedulerGap="+maxSchedulerGap+
                    " allocatedWorkers="+ReadInt(poolProgram,"allocatedSearchWorkers")+
                    " allocatedEdgeCapacity="+ReadInt(poolProgram,"allocatedEdgeCapacity")+
                    " estimatedSearchArrayBytes="+
                    ReadInt(poolProgram,"estimatedSearchArrayBytes")+
                    " estimatedGpuTextureBytes="+
                    ReadInt(poolProgram,"estimatedGpuTextureBytes")+stageMetrics+
                    " warmupPointsThisFrame="+ReadInt(poolProgram,"moveMaskWarmupPointsThisFrame")+
                    " warmupMaxPointsOneFrame="+ReadInt(poolProgram,"maxMoveMaskWarmupPointsOneFrame")+
                    " proofScope=ClientSim-not-clean-VRChat-client");
                Stop(true);
            }
            catch(Exception exception){Fail("concurrent profiler exception: "+exception);}
        }

        private static void StartSearches()
        {
            games=new UdonBehaviour[tableCount];controllers=new UdonBehaviour[tableCount];
            baselineMoves=new int[tableCount];baselineAiMoves=new int[tableCount];
            lastObservedActiveFrame=new int[tableCount];schedulerGrantCounts=new int[tableCount];
            observedSchedulerSlots=new int[tableCount];
            observedSchedulerStrides=new int[tableCount];
            completedVisits=new int[tableCount];
            completionWallMilliseconds=new float[tableCount];
            maxLiveGpuSubmitMilliseconds=new float[tableCount];
            tableCompleted=new bool[tableCount];
            for(int i=0;i<tableCount;i++)
            {lastObservedActiveFrame[i]=-1;observedSchedulerSlots[i]=-1;}
            for(int i=0;i<tableCount;i++)
            {
                GoGame game=pool.tableRoots[i].GetComponentInChildren<GoGame>(true);
                GoAiController ai=pool.tableRoots[i].GetComponentInChildren<GoAiController>(true);
                GoUI ui=pool.tableRoots[i].GetComponentInChildren<GoUI>(true);
                if(game==null||ai==null||ui==null)throw new InvalidOperationException(
                    "table "+i+" runtime is incomplete");
                games[i]=UdonSharpEditorUtility.GetBackingUdonBehaviour(game);
                controllers[i]=UdonSharpEditorUtility.GetBackingUdonBehaviour(ai);
                baselineMoves[i]=ReadInt(games[i],"moveCount");
                baselineAiMoves[i]=ReadInt(controllers[i],"aiMoves");
                Press(FindButton(ui,6));Press(FindButton(ui,34));
            }
            poolProgram.SendCustomEvent("ResetFrameSamples");
            searchesStartedAt=EditorApplication.timeSinceStartup;started=true;
        }

        private static void ObserveSchedulerFrame()
        {
            int running=ReadInt(poolProgram,"localRunningSearches");
            if(running<=0)return;
            if(running>maxObservedRunningSearches)maxObservedRunningSearches=running;
            observedActiveFrames++;
            int dispatches=ReadInt(poolProgram,"localDispatchesPerFrame");
            if(dispatches>maxObservedDispatches)maxObservedDispatches=dispatches;
            bool[] slots=new bool[tableCount];
            for(int i=0;i<tableCount;i++)
            {
                if(ReadInt(controllers[i],"controllerState")!=GoAiController.STATE_THINKING)continue;
                int slot=ReadInt(controllers[i],"schedulerSlot");
                int stride=ReadInt(controllers[i],"schedulerStride");
                if(slot<0||slot>=tableCount||stride<1||stride>tableCount||slots[slot])
                {schedulerObservationFailed=true;continue;}
                slots[slot]=true;observedSchedulerSlots[i]=slot;
                observedSchedulerStrides[i]=stride;
                float live=ReadFloat(ReadReference(controllers[i],"runtime"),
                    "smoothedGpuSubmitMilliseconds");
                if(live>maxLiveGpuSubmitMilliseconds[i])
                    maxLiveGpuSubmitMilliseconds[i]=live;
                if(ReadInt(controllers[i],"schedulerLastDispatchFrame")!=Time.frameCount)
                    continue;
                schedulerGrantCounts[i]++;
                if(lastObservedActiveFrame[i]>=0)
                {
                    int gap=Time.frameCount-lastObservedActiveFrame[i]-1;
                    if(gap>maxSchedulerGap)maxSchedulerGap=gap;
                }
                lastObservedActiveFrame[i]=Time.frameCount;
            }
        }

        private static Button FindButton(GoUI ui,int action)
        {
            for(int i=0;i<ui.controlButtonActions.Length;i++)
                if(ui.controlButtonActions[i]==action)return ui.controlButtons[i];
            throw new InvalidOperationException("button action missing "+action);
        }

        private static void Press(Button button)
        {
            if(button==null||button.onClick==null||button.onClick.GetPersistentEventCount()!=1)
                throw new InvalidOperationException("generated button binding is incomplete");
            button.onClick.Invoke();
        }

        private static string FrameTail()
        {
            float[] samples=poolProgram.GetProgramVariable("frameMillisecondsSamples") as float[];
            int count=ReadInt(poolProgram,"frameSampleCount");
            if(samples==null||count<=0)return "frameTail=unavailable";
            if(count>samples.Length)count=samples.Length;
            float[] values=new float[count];int write=ReadInt(poolProgram,"frameSampleWriteIndex");
            int start=count==samples.Length?write:0;
            float min=float.MaxValue,max=0f,sum=0f;
            for(int i=0;i<count;i++)
            {float v=samples[(start+i)%samples.Length];values[i]=v;if(v<min)min=v;if(v>max)max=v;sum+=v;}
            Array.Sort(values);
            return "frameCount="+count+" frameMinMs="+min.ToString("F3")+
                " frameMeanMs="+(sum/count).ToString("F3")+
                " frameP50Ms="+Percentile(values,0.50f).ToString("F3")+
                " frameP95Ms="+Percentile(values,0.95f).ToString("F3")+
                " frameP99Ms="+Percentile(values,0.99f).ToString("F3")+
                " frameMaxMs="+max.ToString("F3");
        }

        private static float Percentile(float[] sorted,float p)
        {return sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length*p)-1,0,sorted.Length-1)];}
        private static UdonBehaviour ReadReference(UdonBehaviour p,string n)
        {return p.GetProgramVariable(n) as UdonBehaviour;}
        private static int ReadInt(UdonBehaviour p,string n)
        {object v=p==null?null:p.GetProgramVariable(n);return v==null?0:Convert.ToInt32(v);}
        private static float ReadFloat(UdonBehaviour p,string n)
        {object v=p==null?null:p.GetProgramVariable(n);return v==null?0f:Convert.ToSingle(v);}
        private static string ReadString(UdonBehaviour p,string n)
        {object v=p==null?null:p.GetProgramVariable(n);return v==null?"":v.ToString();}
        private static void Timeout(string message)
        {if(EditorApplication.timeSinceStartup>=deadline)Fail(message);}
        private static void Fail(string message)
        {if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_CONCURRENT_PERF_FAIL "+message);Stop(false);}
        private static void Stop(bool pass)
        {
            resultWritten=true;SessionState.SetBool(PendingKey,false);
            EditorApplication.playModeStateChanged-=OnPlayMode;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_CONCURRENT_PERF_RESULT pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
