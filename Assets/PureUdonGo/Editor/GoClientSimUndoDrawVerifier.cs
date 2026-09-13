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
    /// Executes the undo/draw probe through serialized Udon in ClientSim.
    /// </summary>
    public static class GoClientSimUndoDrawVerifier
    {
        private const double TimeoutSeconds=90.0;
        private const string PendingSessionKey="PureUdonGo.ClientSimUndoDrawVerifier.Pending";
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
            Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_REATTACHED");
        }

        [MenuItem("Tools/Pure Udon Go/Verify ClientSim Undo Draw")]
        public static void VerifyClientSimUndoDraw()
        {
            ResetRunner();
            Scene scene=EditorSceneManager.OpenScene(Editor.GoWorldGenerator.ScenePath,OpenSceneMode.Single);
            if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("Generated production scene was not loaded");
            GoGeneratedWorld root=UnityEngine.Object.FindObjectOfType<GoGeneratedWorld>();
            GoGame game=UnityEngine.Object.FindObjectOfType<GoGame>();
            GoAiController controller=UnityEngine.Object.FindObjectOfType<GoAiController>();
            if(root==null||game==null||controller==null)throw new InvalidOperationException("Generated Go runtime objects were not found");
            string programPath=GoClientSimProbeAssetUtility.EnsureProgramAsset(typeof(GoUndoDrawProbe),"UndoDraw");
            GameObject probeObject=new GameObject("ClientSim Undo Draw Probe");probeObject.transform.SetParent(root.transform,false);
            GoUndoDrawProbe probe=probeObject.AddUdonSharpComponent<GoUndoDrawProbe>();probe.game=game;probe.controller=controller;probe.view=game.view;
            GoClientSimProbeAssetUtility.CompileAndCopy(probe,programPath);
            ClientSimSettings.SaveSettings(new ClientSimSettings{enableClientSim=true,displayLogs=true,deleteEditorOnly=false,spawnPlayer=true,hideMenuOnLaunch=true,setTargetFrameRate=false,localPlayerIsMaster=true,isInstanceOwner=true,initializationDelay=0f,currentLanguage="en"});
            SessionState.SetBool(PendingSessionKey,true);EditorSceneManager.SetActiveScene(scene);deadline=EditorApplication.timeSinceStartup+TimeoutSeconds;
            EditorApplication.playModeStateChanged+=OnPlayModeStateChanged;EditorApplication.update+=Pump;
            Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_START scene="+Editor.GoWorldGenerator.ScenePath+" timeoutSeconds="+TimeoutSeconds);
            EditorApplication.isPlaying=true;
        }

        private static void ResetRunner(){EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;EditorApplication.update-=Pump;deadline=0;enteredPlayMode=false;resultWritten=false;probeSent=false;probeProgram=null;}

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode){enteredPlayMode=true;Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_PLAYMODE_ENTERED");}
            else if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(PendingSessionKey,false))Fail("PlayMode ended before undo/draw probe completed");
        }

        private static void Pump()
        {
            if(resultWritten||!EditorApplication.isPlaying)return;
            if(!enteredPlayMode)enteredPlayMode=true;
            try
            {
                if(!ClientSimMain.HasInstance()){FailIfTimedOut("ClientSim did not initialize");return;}
                if(probeProgram==null){GoUndoDrawProbe probe=UnityEngine.Object.FindObjectOfType<GoUndoDrawProbe>();if(probe!=null)probeProgram=UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);}
                if(probeProgram==null){FailIfTimedOut("temporary Udon undo/draw probe was not found");return;}
                if(!probeSent){Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_EVENT name=RunUndoDrawProbe");probeProgram.SendCustomEvent("RunUndoDrawProbe");probeSent=true;return;}
                if(!ReadBool("probeFinished")){FailIfTimedOut("Udon undo/draw probe did not finish");return;}
                bool pvai=ReadBool("pvAiUndoPassed"),preHistory=ReadBool("preUndoHistoryPassed"),postHistory=ReadBool("postUndoHistoryPassed"),history=ReadBool("pvAiUndoHistoryPassed"),undo=ReadBool("pvpUndoConsentPassed"),draw=ReadBool("pvpDrawConsentPassed"),transactional=ReadBool("transactionalUndoPassed"),undoNoCapture=ReadBool("undoNoCapturePresentationPassed");string failure=ReadString("failure");
                Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_RESULT pvAiUndo="+pvai+" preHistory="+preHistory+" postHistory="+postHistory+" pvAiUndoHistory="+history+" post1="+ReadInt("postPrevious1At0")+","+ReadInt("postPrevious1At1")+","+ReadInt("postPrevious1At2")+" post2="+ReadInt("postPrevious2At0")+","+ReadInt("postPrevious2At1")+","+ReadInt("postPrevious2At2")+" mismatch1="+ReadInt("postHistory1Mismatch")+":"+ReadInt("postHistory1MismatchValue")+" mismatch2="+ReadInt("postHistory2Mismatch")+":"+ReadInt("postHistory2MismatchValue")+" pvpUndoConsent="+undo+" pvpDrawConsent="+draw+" transactionalUndo="+transactional+" undoNoCapture="+undoNoCapture+" rebuildReturned="+ReadBool("transactionalRebuildReturned")+" transactionalMismatches="+ReadInt("transactionalMismatchCount")+" failure="+failure);
                if(pvai&&history&&undo&&draw&&transactional&&undoNoCapture){Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_PASS");StopWithResult(true);}else Fail("Udon undo/draw probe failed: "+failure);
            }
            catch(Exception exception){Fail("ClientSim undo/draw inspection exception: "+exception);}
        }

        private static bool ReadBool(string name){object value=probeProgram.GetProgramVariable(name);return value!=null&&Convert.ToBoolean(value);}
        private static int ReadInt(string name){object value=probeProgram.GetProgramVariable(name);return value==null?0:Convert.ToInt32(value);}
        private static string ReadString(string name){object value=probeProgram.GetProgramVariable(name);return value==null?"":value.ToString();}
        private static void FailIfTimedOut(string message){if(EditorApplication.timeSinceStartup>=deadline)Fail(message);}
        private static void Fail(string message){if(resultWritten)return;resultWritten=true;Debug.LogError("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_FAIL "+message);StopWithResult(false);}
        private static void StopWithResult(bool pass){resultWritten=true;SessionState.SetBool(PendingSessionKey,false);EditorApplication.playModeStateChanged-=OnPlayModeStateChanged;EditorApplication.update-=Pump;Debug.Log("PURE_UDON_GO_CLIENTSIM_UNDO_DRAW_RESULT pass="+pass);EditorApplication.Exit(pass?0:1);}
    }
}
#endif
