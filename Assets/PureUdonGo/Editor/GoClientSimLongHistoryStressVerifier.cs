#if UNITY_EDITOR
using System;
using System.IO;
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
    public static class GoClientSimLongHistoryStressVerifier
    {
        private const double OverallTimeoutSeconds=720.0;
        private const string PendingKey="PureUdonGo.LongHistoryStress.Pending";
        private const string FirstCaseKey="PureUdonGo.LongHistoryStress.FirstCase";
        private const string LastCaseKey="PureUdonGo.LongHistoryStress.LastCase";
        private const string ExportKey="PureUdonGo.LongHistoryStress.ExportBuildOnly";
        private static double deadline;
        private static bool enteredPlayMode,resultWritten,probeSent;
        private static UdonBehaviour probeProgram;
        private static int requestedFirstCase;
        private static int requestedLastCase;

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if(!SessionState.GetBool(PendingKey,false))return;
            deadline=EditorApplication.timeSinceStartup+OverallTimeoutSeconds;
            requestedFirstCase=SessionState.GetInt(FirstCaseKey,0);
            requestedLastCase=SessionState.GetInt(LastCaseKey,GoLongHistoryStressProbe.CASE_COUNT);
            EditorApplication.playModeStateChanged-=OnPlayMode;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Long History Stress")]
        public static void VerifyClientSimLongHistoryStress()
        {
            StartStress(0,GoLongHistoryStressProbe.CASE_COUNT);
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Long History Case 64")]
        public static void ProfileClientSimLongHistoryCase64()
        {
            StartStress(2,3);
        }

        [MenuItem("Tools/Pure Udon Go/Profile ClientSim Long History Case 520")]
        public static void ProfileClientSimLongHistoryCase520()
        {
            StartStress(5,6);
        }

        [MenuItem("Tools/Pure Udon Go/Export ClientSim Long History Case 520 Build")]
        public static void ExportClientSimLongHistoryCase520Build()
        {
            StartStress(5,6,true);
        }

        private static void StartStress(int firstCase,int lastCase,bool exportBuildOnly=false)
        {
            Reset();
            requestedFirstCase=firstCase;requestedLastCase=lastCase;
            SessionState.SetInt(FirstCaseKey,firstCase);
            SessionState.SetInt(LastCaseKey,lastCase);
            SessionState.SetBool(ExportKey,exportBuildOnly);
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,
                OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoGame game=pool!=null&&pool.tableRoots!=null&&pool.tableRoots.Length>0?
                pool.tableRoots[0].GetComponentInChildren<GoGame>(true):null;
            GoAiController controller=game==null?null:game.aiController;
            if(root==null||game==null||controller==null||controller.encoder==null)
                throw new InvalidOperationException("Long-history runtime references are incomplete");
            string asset=GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoLongHistoryStressProbe),"Long-history-stress");
            GameObject probeObject=new GameObject("ClientSim Long History Stress Probe");
            probeObject.transform.SetParent(root.transform,false);
            GoLongHistoryStressProbe probe=
                probeObject.AddUdonSharpComponent<GoLongHistoryStressProbe>();
            probe.game=game;probe.controller=controller;probe.encoder=controller.encoder;
            probe.aiSettings=game.aiSettings;
            probe.firstCaseIndex=firstCase;probe.lastCaseExclusive=lastCase;
            probe.exportBuildOnly=exportBuildOnly;
            // Build export is the one-time corpus authoring path. Do not feed
            // it the superseded hand-written prefix; let the existing seeded
            // legal-move generator plus deterministic fallback produce the
            // complete 520-ply log that will be frozen into the performance
            // corpus. Normal stress runs retain their serialized settings.
            if(exportBuildOnly)
            {
                probe.useFixedCasePrefix=false;
                probe.useFixedCase520Moves=false;
                probe.fixedCase520Moves=new int[0];
            }
            GoClientSimProbeAssetUtility.CompileAndCopy(probe,asset);
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,displayLogs=true,deleteEditorOnly=false,
                spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,
                localPlayerIsMaster=true,isInstanceOwner=true,
                initializationDelay=0f,currentLanguage="en"
            });
            SessionState.SetBool(PendingKey,true);EditorSceneManager.SetActiveScene(scene);
            deadline=EditorApplication.timeSinceStartup+OverallTimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_START targets=0,32,64,128,256,520 firstCase="+
                firstCase+" lastCaseExclusive="+lastCase+" exportBuildOnly="+exportBuildOnly+
                " perCaseTimeoutSeconds=90");
            EditorApplication.isPlaying=true;
        }

        private static void Reset()
        {
            EditorApplication.playModeStateChanged-=OnPlayMode;
            EditorApplication.update-=Pump;deadline=0;enteredPlayMode=false;
            resultWritten=false;probeSent=false;probeProgram=null;
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
                if(probeProgram==null)
                {
                    GoLongHistoryStressProbe probe=
                        UnityEngine.Object.FindObjectOfType<GoLongHistoryStressProbe>();
                    if(probe!=null)probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if(probeProgram==null){Timeout("long-history probe was not found");return;}
                if(!probeSent)
                {probeProgram.SendCustomEvent("RunLongHistoryStressProbe");probeSent=true;return;}
                if(!ReadBool("probeFinished"))
                {
                    Timeout("long-history corpus did not finish case="+ReadInt("caseIndex")+
                        " phase="+ReadInt("phase")+" generated="+ReadInt("generatedMoves"));
                    return;
                }
                if(SessionState.GetBool(ExportKey,false))
                {
                    int count=ReadInt("exportedMoveCount");
                    int[] packed=ReadInts("exportedPackedMoves");
                    string root=Directory.GetParent(Application.dataPath).FullName;
                    string path=Path.Combine(root,"go-longhistory-case520-packed.txt");
                    string text=count.ToString();
                    for(int i=0;i<count&&packed!=null&&i<packed.Length;i++)
                        text+=","+packed[i];
                    File.WriteAllText(path,text);
                    int generated=ReadInt("generatedMoves");
                    int caseId=ReadInt("caseIndex");
                    int[] targetArray=ReadInts("targetPlies");
                    string exportFailure=ReadString("failure");
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_BUILD_EXPORT count="+
                        count+" generated="+generated+" caseIndex="+caseId+" target="+
                        (targetArray==null||caseId<0||caseId>=targetArray.Length?"null":targetArray[caseId].ToString())+
                        " probePassed="+ReadBool("probePassed")+" failure="+exportFailure+
                        " path="+path);
                    Stop(true);return;
                }
                bool pass=ReadBool("probePassed");
                int[] targets=ReadInts("targetPlies"),plies=ReadInts("completedPlies");
                int[] legal=ReadInts("legalCounts"),banned=ReadInts("superkoBannedCounts");
                int[] occupied=ReadInts("occupiedCounts"),captures=ReadInts("capturedStones");
                int[] illegal=ReadInts("illegalEmptyCandidates"),ko=ReadInts("koObservations");
                int[] visits=ReadInts("searchVisits"),frames=ReadInts("searchFrames");
                int[] passes=ReadInts("neuralPasses"),graphFrames=ReadInts("gpuGraphFrames");
                int[] historyEntries=ReadInts("historyEntriesExamined");
                int[] historyQueries=ReadInts("historyQueryCounts");
                int[] bucketHits=ReadInts("historyBucketHits");
                int[] bucketMisses=ReadInts("historyBucketMisses");
                int[] fullScans=ReadInts("historyFullScans");
                int[] maxEntries=ReadInts("historyMaxEntriesExamined");
                float[] buildMs=ReadFloats("buildMilliseconds");
                float[] maskMs=ReadFloats("maskMilliseconds");
                float[] featureMs=ReadFloats("featureMilliseconds");
                float[] featureSuperkoMs=ReadFloats("featureSuperkoMilliseconds");
                float[] featureClearMs=ReadFloats("featureClearMilliseconds");
                float[] featureBaseMs=ReadFloats("featureBaseMilliseconds");
                float[] featureLadderMs=ReadFloats("featureLadderMilliseconds");
                float[] featureAreaMs=ReadFloats("featureAreaMilliseconds");
                int[] featureSteps=ReadInts("featureSteps");
                int[] ladderTransitions=ReadInts("ladderTransitions");
                int[] ladderNodes=ReadInts("ladderNodes");
                int[] ladderDfsCalls=ReadInts("ladderDfsCalls");
                int[] maxLadderTransitionsOneSlice=ReadInts("maxLadderTransitionsOneSlice");
                int[] ladderCacheLookups=ReadInts("ladderCacheLookups");
                int[] ladderCacheEntriesChecked=ReadInts("ladderCacheEntriesChecked");
                int[] ladderCacheKeyComparisons=ReadInts("ladderCacheKeyComparisons");
                int[] ladderCacheFullHits=ReadInts("ladderCacheFullHits");
                int[] ladderCacheMarkerHits=ReadInts("ladderCacheMarkerHits");
                int[] ladderCacheWorkingOnlyHits=ReadInts("ladderCacheWorkingOnlyHits");
                float[] featureActiveCpuMs=ReadFloats("featureActiveCpuMilliseconds");
                float[] featureLadderActiveCpuMs=ReadFloats("featureLadderActiveCpuMilliseconds");
                int[] livePhase=ReadInts("liveSearchPhase");
                int[] liveVisits=ReadInts("liveSearchVisits");
                int[] liveSteps=ReadInts("liveSearchStepCalls");
                int[] liveLegal=ReadInts("liveLegalMoveChecks");
                int[] liveSuperkoPoints=ReadInts("liveSuperkoPoints");
                int[] liveSuperkoFrames=ReadInts("liveSuperkoFrames");
                int[] liveGpuPasses=ReadInts("liveGpuPasses");
                int[] liveGpuFrames=ReadInts("liveGpuGraphFrames");
                int[] liveHistoryQueries=ReadInts("liveHistoryQueries");
                int[] liveHistoryHits=ReadInts("liveHistoryBucketHits");
                int[] liveHistoryMisses=ReadInts("liveHistoryBucketMisses");
                int[] liveHistoryScans=ReadInts("liveHistoryFullScans");
                int[] liveHistoryMax=ReadInts("liveHistoryMaxEntries");
                int[] liveEncoderState=ReadInts("liveEncoderState");
                int[] liveEncoderSteps=ReadInts("liveEncoderSteps");
                int[] liveEncoderTransitions=ReadInts("liveEncoderLadderTransitions");
                int[] liveEncoderNodes=ReadInts("liveEncoderLadderNodes");
                int[] liveEncoderHits=ReadInts("liveEncoderCacheHits");
                int[] liveEncoderMisses=ReadInts("liveEncoderCacheMisses");
                float[] liveFeatureMs=ReadFloats("liveFeatureMilliseconds");
                float[] liveLadderMs=ReadFloats("liveLadderMilliseconds");
                float[] liveSuperkoMs=ReadFloats("liveSuperkoMilliseconds");
                float[] liveGpuUploadMs=ReadFloats("liveGpuUploadMilliseconds");
                float[] liveGpuDispatchMs=ReadFloats("liveGpuDispatchMilliseconds");
                float[] liveReadbackMs=ReadFloats("liveReadbackMilliseconds");
                float[] liveMctsMs=ReadFloats("liveMctsMilliseconds");
                float[] milliseconds=ReadFloats("searchMilliseconds");
                float[] searchFeatureMs=ReadFloats("searchFeatureMilliseconds");
                float[] searchFeatureActiveCpuMs=ReadFloats("searchFeatureActiveCpuMilliseconds");
                float[] searchLadderActiveCpuMs=ReadFloats("searchLadderActiveCpuMilliseconds");
                float[] searchGpuUploadMs=ReadFloats("searchGpuUploadMilliseconds");
                float[] searchGpuDispatchMs=ReadFloats("searchGpuDispatchMilliseconds");
                float[] searchReadbackMs=ReadFloats("searchReadbackMilliseconds");
                float[] searchMctsSelectionMs=ReadFloats("searchMctsSelectionMilliseconds");
                float[] searchMctsExpansionMs=ReadFloats("searchMctsExpansionMilliseconds");
                float[] searchMctsBackupMs=ReadFloats("searchMctsBackupMilliseconds");
                float[] searchRootCopyMs=ReadFloats("searchRootCopyMilliseconds");
                float[] searchScoreUtilityMs=ReadFloats("searchScoreUtilityMilliseconds");
                float[] searchTickMaxMs=ReadFloats("searchTickMaxMilliseconds");
                float[] checksums=ReadFloats("featureChecksums");
                for(int i=0;i<GoLongHistoryStressProbe.CASE_COUNT;i++)
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_CASE index="+i+
                        " targetPlies="+targets[i]+" completedPlies="+plies[i]+
                        " occupied="+occupied[i]+" legal="+legal[i]+
                        " superkoBanned="+banned[i]+" captures="+captures[i]+
                        " illegalEmpty="+illegal[i]+" ko="+ko[i]+
                        " featureChecksum="+checksums[i]+" visits="+visits[i]+
                        " searchFrames="+frames[i]+" searchMs="+milliseconds[i]+
                        " searchFeatureMs="+searchFeatureMs[i]+" searchFeatureActiveCpuMs="+
                        searchFeatureActiveCpuMs[i]+" searchLadderActiveCpuMs="+
                        searchLadderActiveCpuMs[i]+" searchGpuUploadMs="+
                        searchGpuUploadMs[i]+" searchGpuDispatchMs="+searchGpuDispatchMs[i]+
                        " searchReadbackMs="+searchReadbackMs[i]+" searchMctsSelectionMs="+
                        searchMctsSelectionMs[i]+" searchMctsExpansionMs="+searchMctsExpansionMs[i]+
                        " searchMctsBackupMs="+searchMctsBackupMs[i]+" searchRootCopyMs="+
                        searchRootCopyMs[i]+" searchScoreUtilityMs="+searchScoreUtilityMs[i]+
                        " searchTickMaxMs="+searchTickMaxMs[i]+
                        " buildMs="+buildMs[i]+" maskMs="+maskMs[i]+" featureMs="+featureMs[i]+
                        " featureSuperkoMs="+featureSuperkoMs[i]+" featureClearMs="+featureClearMs[i]+
                        " featureBaseMs="+featureBaseMs[i]+" featureLadderMs="+featureLadderMs[i]+" featureAreaMs="+
                        featureAreaMs[i]+" featureSteps="+featureSteps[i]+" ladderTransitions="+
                        ladderTransitions[i]+" ladderNodes="+ladderNodes[i]+" featureActiveCpuMs="+
                        featureActiveCpuMs[i]+" featureLadderActiveCpuMs="+featureLadderActiveCpuMs[i]+
                        " ladderDfsCalls="+ladderDfsCalls[i]+" maxLadderTransitionsOneSlice="+
                        maxLadderTransitionsOneSlice[i]+" ladderCacheLookups="+ladderCacheLookups[i]+
                        " ladderCacheEntriesChecked="+ladderCacheEntriesChecked[i]+" ladderCacheKeyComparisons="+
                        ladderCacheKeyComparisons[i]+" ladderCacheFullHits="+ladderCacheFullHits[i]+
                        " ladderCacheMarkerHits="+ladderCacheMarkerHits[i]+" ladderCacheWorkingOnlyHits="+
                        ladderCacheWorkingOnlyHits[i]+
                        " neuralPasses="+passes[i]+" gpuGraphFrames="+graphFrames[i]+" historyEntriesExamined="+
                        (historyEntries==null?0:historyEntries[i])+" historyQueries="+historyQueries[i]+
                        " bucketHits="+bucketHits[i]+" bucketMisses="+bucketMisses[i]+" fullScans="+
                        fullScans[i]+" historyMaxEntries="+maxEntries[i]+" liveSearchPhase="+
                        livePhase[i]+" liveVisits="+liveVisits[i]+" liveSteps="+liveSteps[i]+
                        " liveLegal="+liveLegal[i]+" liveSuperkoPoints="+liveSuperkoPoints[i]+
                        " liveSuperkoFrames="+liveSuperkoFrames[i]+" liveGpuPasses="+liveGpuPasses[i]+
                        " liveGpuFrames="+liveGpuFrames[i]+" liveHistoryQueries="+liveHistoryQueries[i]+
                        " liveHistoryHits="+liveHistoryHits[i]+" liveHistoryMisses="+liveHistoryMisses[i]+
                        " liveHistoryFullScans="+liveHistoryScans[i]+" liveHistoryMax="+liveHistoryMax[i]+
                        " liveEncoderState="+liveEncoderState[i]+" liveEncoderSteps="+liveEncoderSteps[i]+
                        " liveEncoderTransitions="+liveEncoderTransitions[i]+" liveEncoderNodes="+
                        liveEncoderNodes[i]+" liveEncoderCacheHits="+liveEncoderHits[i]+" liveEncoderCacheMisses="+
                        liveEncoderMisses[i]+
                        " liveFeatureMs="+liveFeatureMs[i]+" liveLadderMs="+liveLadderMs[i]+" liveSuperkoMs="+
                        liveSuperkoMs[i]+" liveGpuUploadMs="+liveGpuUploadMs[i]+" liveGpuDispatchMs="+
                        liveGpuDispatchMs[i]+" liveReadbackMs="+liveReadbackMs[i]+" liveMctsMs="+liveMctsMs[i]);
                string failure=ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_RESULT pass="+pass+
                    " failure="+failure);
                if(!pass){Fail(failure);return;}
                Debug.Log("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_PASS cases="+
                    (requestedLastCase-requestedFirstCase)+" targets=0,32,64,128,256,520 visitsEach="+
                    GoLongHistoryStressProbe.SEARCH_VISITS);
                Stop(true);
            }
            catch(Exception exception){Fail("long-history inspection exception: "+exception);}
        }

        private static int ReadInt(string n)
        {object v=probeProgram.GetProgramVariable(n);return v==null?0:Convert.ToInt32(v);}
        private static bool ReadBool(string n)
        {object v=probeProgram.GetProgramVariable(n);return v!=null&&Convert.ToBoolean(v);}
        private static string ReadString(string n)
        {object v=probeProgram.GetProgramVariable(n);return v==null?"":v.ToString();}
        private static int[] ReadInts(string n){return probeProgram.GetProgramVariable(n) as int[];}
        private static float[] ReadFloats(string n){return probeProgram.GetProgramVariable(n) as float[];}
        private static void Timeout(string message)
        {if(EditorApplication.timeSinceStartup>=deadline)Fail(message);}
        private static void Fail(string message)
        {if(resultWritten)return;Debug.LogError("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_FAIL "+message);Stop(false);}
        private static void Stop(bool pass)
        {
            resultWritten=true;SessionState.SetBool(PendingKey,false);
            EditorApplication.playModeStateChanged-=OnPlayMode;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_LONG_HISTORY_FINAL pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
