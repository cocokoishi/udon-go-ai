#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

public static class GoNetworkAuthorityCheck
{
    private static int failures;

    private static void Expect(bool condition,string message)
    {
        if(condition)return;
        failures++;
        Debug.LogError("PURE_UDON_GO_NETWORK_AUTHORITY_FAIL "+message);
    }

    public static void Run()
    {
        failures=0;
        GameObject remoteObject=null, ownerObject=null, stateObject=null, searchObject=null, aiObject=null;
        try
        {
            Networking.LocalPlayer=new VRCPlayerApi();
            Networking.LocalPlayer.playerId=7;
            Networking.LocalPlayer.displayName="Authority Tester";
            Networking.LocalPlayer.isLocal=true;
            Networking.IsMaster=false;
            Networking.LocalPlayerOwnsObjects=false;
            Networking.OwnershipTransferSucceeds=false;

            remoteObject=new GameObject("GoRemoteStartup");
            var remote=remoteObject.AddComponent<PureUdonGo.GoGame>();
            remote.Start();
            Expect(remote.revision==0,"non-owner startup does not synthesize a local revision");
            Expect(remote.positionHistoryCount==0,"non-owner startup waits for authoritative history");
            Expect(!remote.TryPlay(0),"non-owner move is rejected when ownership transfer fails");
            Expect(remote.board[0]==PureUdonGo.GoGame.EMPTY,"rejected non-owner move leaves board unchanged");

            Networking.OwnershipTransferSucceeds=true;
            Networking.LocalPlayerOwnsObjects=true;
            ownerObject=new GameObject("GoOwnerMutation");
            var owner=ownerObject.AddComponent<PureUdonGo.GoGame>();
            owner.blackIsAI=false;
            owner.whiteIsAI=false;
            owner.matchStarted=true;
            owner.Start();
            int beforeRevision=owner.revision;
            Expect(owner.TryPlay(0),"owner move is accepted after ownership is acquired");
            Expect(owner.board[0]==PureUdonGo.GoGame.BLACK,"owner move writes authoritative board");
            Expect(owner.revision>beforeRevision,"owner move advances revision");

            stateObject=new GameObject("GoNetworkAuthorityState");
            var state=stateObject.AddComponent<PureUdonGo.GoSearchState>();
            searchObject=new GameObject("GoNetworkAuthoritySearch");
            var search=searchObject.AddComponent<PureUdonGo.GoMctsSearch>();
            search.simulationState=state;
            aiObject=new GameObject("GoNetworkAuthorityAi");
            var ai=aiObject.AddComponent<PureUdonGo.GoAiController>();
            ai.game=owner;
            ai.search=search;
            ai.autoStart=false;
            owner.aiController=ai;
            Expect(search.BeginSearch(owner,2,5,owner.settingsRevision),"search begins before network snapshot callback");
            Expect(search.phase==PureUdonGo.GoMctsSearch.PHASE_WAITING_NEURAL,"search is waiting for neural output");
            owner.OnDeserialization();
            Expect(search.phase==PureUdonGo.GoMctsSearch.PHASE_CANCELLED,"deserialization invalidates old search");
            Debug.Log("PURE_UDON_GO_NETWORK_AUTHORITY_METRICS remoteRevision="+remote.revision+
                " ownerRevision="+owner.revision+" stalePhase="+search.phase+
                " ownerLifecycle="+ai.ownerLifecycle);
        }
        catch(Exception exception)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_NETWORK_AUTHORITY_FAIL exception="+exception);
        }
        finally
        {
            Networking.OwnershipTransferSucceeds=true;
            Networking.LocalPlayerOwnsObjects=true;
            Networking.IsMaster=true;
            Networking.LocalPlayer=null;
            if(aiObject!=null)UnityEngine.Object.DestroyImmediate(aiObject);
            if(searchObject!=null)UnityEngine.Object.DestroyImmediate(searchObject);
            if(stateObject!=null)UnityEngine.Object.DestroyImmediate(stateObject);
            if(ownerObject!=null)UnityEngine.Object.DestroyImmediate(ownerObject);
            if(remoteObject!=null)UnityEngine.Object.DestroyImmediate(remoteObject);
            if(failures==0){Debug.Log("PURE_UDON_GO_NETWORK_AUTHORITY_PASS failures=0");EditorApplication.Exit(0);}
            else{Debug.LogError("PURE_UDON_GO_NETWORK_AUTHORITY_FAIL count="+failures);EditorApplication.Exit(6);}
        }
    }
}
#endif
