using UdonSharp;

namespace PureUdonGo
{
    /// <summary>Single generated world-space button action.</summary>
    public sealed class GoUiButton : UdonSharpBehaviour
    {
        public GoUI ui;
        public int action;

        public void Start()
        {
            // These controls are world-space UGUI targets.  They still use
            // Button.onClick -> Press, but must not advertise a second
            // VRChat Interact/Use affordance or outline.
            DisableInteractive=true;
        }

        public override void Interact()
        {
            if(ui!=null)ui.HandleAction(action);
        }

        // UnityEngine.UI.Button invokes this through a persistent Udon event
        // installed by the production generator. Board and control/status
        // canvases own only their UIShape hit surfaces; buttons remain
        // ordinary UGUI targets and table geometry has no solid collider.
        public void Press()
        {
            if(ui!=null)ui.HandleAction(action);
        }
    }
}
