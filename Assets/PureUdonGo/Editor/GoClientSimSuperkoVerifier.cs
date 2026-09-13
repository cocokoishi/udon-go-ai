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
    /// <summary>
    /// Runs the long-history cooperative superko probe through compiled Udon
    /// in ClientSim. PASS covers exact mask parity with GoGame, multi-frame
    /// preparation, one complete neural leaf, and cancellation of the next
    /// in-progress mask after a revision reset.
    /// </summary>
    public static class GoClientSimSuperkoVerifier
    {
        private const double TimeoutSeconds=240.0;
        private const string PendingSessionKey=
            "PureUdonGo.ClientSimSuperkoVerifier.Pending";
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
            EditorApplication.update-=Pump;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_REATTACHED");
        }

[MenuItem("Tools/Pure Udon Go/Tests/Verify ClientSim Cooperative Superko")]
        public static void VerifyClientSimCooperativeSuperko()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(
                Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException(
                    "Generated production scene was not loaded: "+
                    Editor.GoWorldGenerator.ScenePath);
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            GoGame game=pool!=null&&pool.tableRoots!=null&&
                pool.tableRoots.Length>0&&pool.tableRoots[0]!=null
                ?pool.tableRoots[0].GetComponentInChildren<GoGame>(true)
                :UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller=game==null?null:
                game.GetComponentInChildren<GoAiController>(true);
            if(root==null||game==null||controller==null)
                throw new InvalidOperationException(
                    "Generated Go superko runtime objects were not found");

            string probeAssetPath=GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoSuperkoCooperativeProbe),"Superko-cooperative");
            GameObject probeObject=new GameObject("ClientSim Cooperative Superko Probe");
            probeObject.transform.SetParent(root.transform,false);
            GoSuperkoCooperativeProbe probe=
                probeObject.AddUdonSharpComponent<GoSuperkoCooperativeProbe>();
            probe.game=game;probe.controller=controller;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe,probeAssetPath);

            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,
                displayLogs=true,
                deleteEditorOnly=false,
                spawnPlayer=true,
                hideMenuOnLaunch=true,
                setTargetFrameRate=false,
                localPlayerIsMaster=true,
                isInstanceOwner=true,
                initializationDelay=0f,
                currentLanguage="en"
            });
            SessionState.SetBool(PendingSessionKey,true);
            EditorSceneManager.SetActiveScene(scene);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_START scene="+
                Editor.GoWorldGenerator.ScenePath+" historyMoves=160 timeoutSeconds="+
                TimeoutSeconds);
            EditorApplication.isPlaying=true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            deadline=0.0;enteredPlayMode=false;resultWritten=false;
            probeSent=false;probeProgram=null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode=true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_PLAYMODE_ENTERED");
            }
            else if(state==PlayModeStateChange.EnteredEditMode&&
                SessionState.GetBool(PendingSessionKey,false))
                Fail("PlayMode ended before cooperative superko verification completed");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)
            {
                enteredPlayMode=true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_PLAYMODE_ENTERED");
            }
            try
            {
                if(!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");
                    return;
                }
                if(probeProgram==null)
                {
                    GoSuperkoCooperativeProbe probe=
                        UnityEngine.Object.FindObjectOfType<GoSuperkoCooperativeProbe>();
                    if(probe!=null)probeProgram=
                        UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if(probeProgram==null)
                {
                    FailIfTimedOut("temporary Udon superko probe was not found");
                    return;
                }
                if(!probeSent)
                {
                    probeProgram.SendCustomEvent("RunSuperkoCooperativeProbe");
                    probeSent=true;
                    return;
                }
                if(!ReadBool("probeFinished"))
                {
                    FailIfTimedOut("cooperative superko probe did not finish phase="+
                        ReadInt("phase")+" history="+ReadInt("historyMoveIndex")+
                        " reference="+ReadInt("referenceCursor"));
                    return;
                }
                bool passed=ReadBool("probePassed");
                string metrics="historyMoves="+ReadInt("historyMoveIndex")+
                    " points="+ReadInt("firstMaskPoints")+
                    " frames="+ReadInt("firstMaskFrames")+
                    " milliseconds="+ReadFloat("firstMaskMilliseconds")+
                    " maxPointsOneFrame="+ReadInt("observedMaxPointsOneFrame")+
                    " maxFrameMilliseconds="+ReadFloat("observedMaxFrameMilliseconds")+
                    " mismatches="+ReadInt("maskMismatches")+
                    " rootStateCopiedInts="+ReadInt("rootStateCopiedInts")+
                    " rootHistoryCopiedEntries="+ReadInt("rootHistoryCopiedEntries")+
                    " simulationUndoOperations="+ReadInt("simulationUndoOperations")+
                    " historyIndexEntries="+ReadInt("historyIndexEntries")+
                    " historyIndexMismatches="+ReadInt("historyIndexMismatches")+
                    " neuralStages="+ReadInt("completedNeuralLeafStages")+
                    " cancelledAt="+ReadInt("cancelledAtPoint")+
                    " pointsAfterCancel="+ReadInt("pointsAfterCancellation")+
                    " cancelledProbes="+ReadInt("cancelledProbeCount")+
                    " revision="+ReadInt("revisionBeforeCancellation")+"->"+
                    ReadInt("revisionAfterCancellation")+
                    " failure="+ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_RESULT pass="+passed+" "+metrics);
                if(!passed)
                {
                    Fail("serialized Udon cooperative superko probe failed: "+metrics);
                    return;
                }
                Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_PASS "+metrics);
                StopWithResult(true);
            }
            catch(Exception exception)
            {
                Fail("ClientSim cooperative superko inspection exception: "+exception);
            }
        }

        private static int ReadInt(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value==null?0:Convert.ToInt32(value);
        }

        private static float ReadFloat(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value==null?0f:Convert.ToSingle(value);
        }

        private static bool ReadBool(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value!=null&&Convert.ToBoolean(value);
        }

        private static string ReadString(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value==null?"":value.ToString();
        }

        private static void FailIfTimedOut(string message)
        {
            if(EditorApplication.timeSinceStartup<deadline)return;
            Fail(message);
        }

        private static void Fail(string message)
        {
            if(resultWritten)return;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_SUPERKO_FAIL "+message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten=true;
            SessionState.SetBool(PendingSessionKey,false);
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_SUPERKO_FINAL pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
