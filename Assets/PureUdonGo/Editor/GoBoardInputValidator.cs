#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace PureUdonGo.Editor
{
    // Shared by the generator and production validator. No count-only pass:
    // verify every bound location, callback target and physical coordinate.
    // Production boards have one UIShape hit collider, and it must be a
    // trigger. The single world-space UGUI receiver remains the only board
    // input path; stones, grid and table pedestal stay collider-free.
    public static class GoBoardInputValidator
    {
        public static void Validate(GoBoardView view)
        {
            if(view==null||view.game==null)throw new InvalidOperationException("Missing board input view/game");
            GoBoardCell[] cells=view.GetComponentsInChildren<GoBoardCell>(true);
            GoBoardInput[] receivers=view.GetComponentsInChildren<GoBoardInput>(true);
            GoBoardInput receiver=view.inputReceiver;
            if(receiver==null||cells.Length!=0||receivers.Length!=1||receivers[0]!=receiver||
                receiver.game!=view.game||receiver.view!=view)
                throw new InvalidOperationException("Board must have one UGUI receiver and zero legacy collider cells");
            Collider[] colliders=view.GetComponentsInChildren<Collider>(true);
            BoxCollider boardCollider=receiver.GetComponent<BoxCollider>();
            if(colliders.Length!=1||colliders[0]!=boardCollider||boardCollider==null||
                !boardCollider.enabled||!boardCollider.isTrigger)
                throw new InvalidOperationException("Board must contain only its enabled UIShape trigger collider");
            Canvas canvas=receiver.GetComponent<Canvas>();
            if(canvas==null||canvas.renderMode!=RenderMode.WorldSpace||
                receiver.GetComponent<GraphicRaycaster>()==null||receiver.GetComponent<VRCUiShape>()==null)
                throw new InvalidOperationException("Missing VRChat world-space board raycaster");
            if(Vector3.Dot(canvas.transform.TransformDirection(Vector3.back),view.transform.up)<0.999f)
                throw new InvalidOperationException("Board input Canvas raycast side is not facing up");
            UdonBehaviour backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver);
            if(backing==null)
                throw new InvalidOperationException("Central receiver backing UdonBehaviour is missing");
            if(receiver.GetComponentsInChildren<Button>(true).Length!=GoGame.AREA)
                throw new InvalidOperationException("UGUI board must contain 361 hit targets");
            for(int loc=0;loc<GoGame.AREA;loc++)
            {
                Transform hit=receiver.transform.Find("Intersection "+loc.ToString("000"));
                if(hit==null)throw new InvalidOperationException("Missing UGUI hit target "+loc);
                Button button=hit.GetComponent<Button>();EventTrigger trigger=hit.GetComponent<EventTrigger>();
                if(button==null||trigger==null||trigger.triggers.Count!=2||!button.interactable||
                    button.image==null||!button.image.raycastTarget||button.navigation.mode!=Navigation.Mode.None)
                    throw new InvalidOperationException("Invalid UGUI target "+loc);
                ValidateEvent(button.onClick,backing);
                ValidateArgument(new SerializedObject(button),"m_OnClick", "Click"+loc.ToString("000"));
                bool enter=false,exit=false;
                for(int i=0;i<trigger.triggers.Count;i++)
                {
                    EventTrigger.Entry e=trigger.triggers[i];
                    string name;
                    if(e.eventID==EventTriggerType.PointerEnter&&!enter){enter=true;name="Hover";}
                    else if(e.eventID==EventTriggerType.PointerExit&&!exit){exit=true;name="Exit";}
                    else throw new InvalidOperationException("Unexpected/duplicate pointer callback");
                    ValidateEvent(e.callback,backing);
                    ValidateArgument(new SerializedObject(trigger),"m_Delegates.Array.data["+i+"].callback",name+loc.ToString("000"));
                }
                Vector3 p=receiver.transform.parent.InverseTransformPoint(hit.position);
                Vector3 stone=view.stoneObjects[loc].transform.localPosition;
                if(Mathf.Abs(p.x-stone.x)>0.0001f||Mathf.Abs(p.z-stone.z)>0.0001f||
                    Mathf.Abs(p.y-0.176f)>0.0001f)
                    throw new InvalidOperationException("UGUI/stone coordinate mismatch "+loc);
            }
        }

        private static void ValidateEvent(UnityEventBase e,UdonBehaviour receiver)
        {
            if(e.GetPersistentEventCount()!=1||e.GetPersistentTarget(0)!=receiver||
                e.GetPersistentMethodName(0)!="SendCustomEvent")
                throw new InvalidOperationException("Board callback must target its own backing Udon receiver");
        }

        private static void ValidateArgument(SerializedObject target,string field,string expected)
        {
            SerializedProperty argument=target.FindProperty(field+".m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
            if(argument==null||argument.stringValue!=expected||typeof(GoBoardInput).GetMethod(expected)==null)
                throw new InvalidOperationException("Board callback location mismatch: "+expected);
        }
    }
}
#endif
