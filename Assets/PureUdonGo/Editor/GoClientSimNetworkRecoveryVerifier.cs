#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

namespace PureUdonGo
{
    /// <summary>
    /// Drives the real Udon recovery fixture through ClientSim player leave
    /// callbacks. It deliberately reports a single-VM simulation, not network
    /// transport between two independent VRChat clients.
    /// </summary>
    public static class GoClientSimNetworkRecoveryVerifier
    {
        private const double TimeoutSeconds=90.0;
        private const string PendingSessionKey="PureUdonGo.ClientSimNetworkRecoveryVerifier.Pending";
        private const string StepSessionKey="PureUdonGo.ClientSimNetworkRecoveryVerifier.Step";

        private static double deadline;
        private static bool enteredPlayMode;
        private static bool resultWritten;
        private static bool remoteSpawnRequested;
        private static int step;
        private static UdonBehaviour probeProgram;
        private static VRCPlayerApi localPlayer;
        private static VRCPlayerApi remotePlayer;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if(!SessionState.GetBool(PendingSessionKey,false))return;
            step=SessionState.GetInt(StepSessionKey,0);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_REATTACHED step="+step);
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Reconnect Recovery")]
        public static void VerifyClientSimReconnectRecovery()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)
                throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoBoardPool pool=UnityEngine.Object.FindObjectOfType<GoBoardPool>();
            if(root==null||pool==null||pool.tableRoots==null||pool.tableRoots.Length<3)
                throw new InvalidOperationException("Generated Go board pool does not expose three table roots");
            GoGame undoGame=pool.tableRoots[0]==null?null:
                pool.tableRoots[0].GetComponentInChildren<GoGame>(true);
            GoGame drawGame=pool.tableRoots[1]==null?null:
                pool.tableRoots[1].GetComponentInChildren<GoGame>(true);
            GoGame aiGame=pool.tableRoots[2]==null?null:
                pool.tableRoots[2].GetComponentInChildren<GoGame>(true);
            if(undoGame==null||drawGame==null||aiGame==null)
                throw new InvalidOperationException("Generated Go recovery fixture tables were not found");

            string programPath=GoClientSimProbeAssetUtility.EnsureProgramAsset(
                typeof(GoNetworkRecoveryProbe),"NetworkRecovery");
            GameObject probeObject=new GameObject("ClientSim Network Recovery Probe");
            probeObject.transform.SetParent(root.transform,false);
            GoNetworkRecoveryProbe probe=probeObject.AddUdonSharpComponent<GoNetworkRecoveryProbe>();
            probe.undoGame=undoGame;probe.drawGame=drawGame;probe.aiGame=aiGame;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe,programPath);
            ClientSimSettings.SaveSettings(new ClientSimSettings
            {
                enableClientSim=true,displayLogs=true,deleteEditorOnly=false,
                spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,
                localPlayerIsMaster=true,isInstanceOwner=true,initializationDelay=0f,
                currentLanguage="en"
            });
            SessionState.SetInt(StepSessionKey,0);
            SessionState.SetBool(PendingSessionKey,true);
            EditorSceneManager.SetActiveScene(scene);
            deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;
            EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_START scene="+
                Editor.GoWorldGenerator.ScenePath+" timeoutSeconds="+TimeoutSeconds);
            EditorApplication.isPlaying=true;
        }

        private static void ResetRunner()
        {
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            deadline=0;enteredPlayMode=false;resultWritten=false;
            remoteSpawnRequested=false;step=0;probeProgram=null;localPlayer=null;remotePlayer=null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                enteredPlayMode=true;
                Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_PLAYMODE_ENTERED");
            }
            else if(state==PlayModeStateChange.EnteredEditMode&&
                SessionState.GetBool(PendingSessionKey,false))
            {
                Fail("PlayMode ended before reconnect recovery completed");
            }
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance())
                {
                    FailIfTimedOut("ClientSim did not initialize");return;
                }
                if(probeProgram==null)
                {
                    GoNetworkRecoveryProbe probe=
                        UnityEngine.Object.FindObjectOfType<GoNetworkRecoveryProbe>();
                    if(probe!=null)
                        probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
                }
                if(probeProgram==null)
                {
                    FailIfTimedOut("temporary Udon network recovery probe was not found");return;
                }
                if(step==0)
                {
                    localPlayer=Networking.LocalPlayer;
                    if(localPlayer==null)
                    {
                        FailIfTimedOut("ClientSim local player was not spawned");return;
                    }
                    if(!remoteSpawnRequested)
                    {
                        ClientSimMain.SpawnRemotePlayer("Go Recovery Witness");
                        remoteSpawnRequested=true;
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_EVENT name=SpawnRemotePlayer");
                    }
                    SetStep(1);return;
                }
                if(step==1)
                {
                    remotePlayer=FindRemotePlayer();
                    if(remotePlayer==null)
                    {
                        FailIfTimedOut("ClientSim remote recovery witness was not spawned");return;
                    }
                    probeProgram.SetProgramVariable("departingPlayerId",remotePlayer.playerId);
                    probeProgram.SetProgramVariable("departingPlayerName",remotePlayer.displayName);
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_REMOTE id="+remotePlayer.playerId+
                        " name="+remotePlayer.displayName);
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_EVENT name=RunNetworkRecoveryProbe");
                    probeProgram.SendCustomEvent("RunNetworkRecoveryProbe");
                    SetStep(2);return;
                }
                if(step==2)
                {
                    if(!ReadBool("prepared"))
                    {
                        if(ReadBool("probeFinished"))
                        {
                            Fail("Udon recovery fixture setup failed: "+ReadString("failure"));
                        }
                        else FailIfTimedOut("Udon recovery fixture did not prepare");
                        return;
                    }
                    remotePlayer=FindRemotePlayer();
                    if(remotePlayer==null)
                    {
                        Fail("remote player disappeared before leave fixture removal");return;
                    }
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_EVENT name=RemoveRemotePlayer remoteId="+
                        remotePlayer.playerId);
                    ClientSimMain.RemovePlayer(remotePlayer);
                    SetStep(3);return;
                }
                if(step==3)
                {
                    if(!ReadBool("probeFinished"))
                    {
                        FailIfTimedOut("Udon recovery probe did not observe local player leave");return;
                    }
                    bool passed=ReadBool("probePassed");
                    Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_RESULT prepared="+
                        ReadBool("prepared")+" probePassed="+passed+
                        " undoSeatAfter="+ReadInt("undoSeatAfter")+
                        " drawSeatAfter="+ReadInt("drawSeatAfter")+
                        " undoOfferCleared="+ReadBool("undoOfferCleared")+
                        " drawOfferCleared="+ReadBool("drawOfferCleared")+
                        " aiAuthorityAfter="+ReadInt("aiAuthorityAfter")+
                        " recovery="+ReadInt("undoRecoveryBefore")+"->"+
                        ReadInt("undoRecoveryAfter")+","+ReadInt("drawRecoveryBefore")+"->"+
                        ReadInt("drawRecoveryAfter")+","+ReadInt("aiRecoveryBefore")+"->"+
                        ReadInt("aiRecoveryAfter")+" failure="+ReadString("failure"));
                    if(passed)
                    {
                        Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_PASS");
                        StopWithResult(true);
                    }
                    else Fail("Udon reconnect recovery probe failed: "+ReadString("failure"));
                    return;
                }
                Fail("unknown reconnect recovery verifier state="+step);
            }
            catch(Exception exception)
            {
                Fail("ClientSim reconnect recovery inspection exception: "+exception);
            }
        }

        private static bool ReadBool(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value!=null&&Convert.ToBoolean(value);
        }

        private static int ReadInt(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value==null?0:Convert.ToInt32(value);
        }

        private static string ReadString(string name)
        {
            object value=probeProgram.GetProgramVariable(name);
            return value==null?"":value.ToString();
        }

        private static VRCPlayerApi FindRemotePlayer()
        {
            for(int i=0;i<VRCPlayerApi.AllPlayers.Count;i++)
            {
                VRCPlayerApi player=VRCPlayerApi.AllPlayers[i];
                if(player!=null&&!player.isLocal)return player;
            }
            return null;
        }

        private static void SetStep(int value)
        {
            step=value;SessionState.SetInt(StepSessionKey,value);
        }

        private static void FailIfTimedOut(string message)
        {
            if(EditorApplication.timeSinceStartup>=deadline)Fail(message);
        }

        private static void Fail(string message)
        {
            if(resultWritten)return;
            resultWritten=true;
            Debug.LogError("PURE_UDON_GO_CLIENTSIM_RECOVERY_FAIL "+message);
            StopWithResult(false);
        }

        private static void StopWithResult(bool pass)
        {
            resultWritten=true;
            SessionState.SetBool(PendingSessionKey,false);
            EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;
            EditorApplication.update-=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_RECOVERY_FINAL pass="+pass);
            EditorApplication.Exit(pass?0:1);
        }
    }
}
#endif
