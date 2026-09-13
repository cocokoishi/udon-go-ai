using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Legacy/test interaction endpoint for one board intersection. Production
    /// scenes use the single GoBoardInput UGUI receiver with its UIShape
    /// trigger; keeping this type lets old isolated fixtures deserialize
    /// without reintroducing solid board colliders in the generated world.
    /// </summary>
    public sealed class GoBoardCell : UdonSharpBehaviour
    {
        public GoGame game;
        public GoBoardView view;
        public int location;

        public override void Interact()
        {
            if(game==null)return;
            bool legal=game.IsLegalMove(location);
            if(!legal){if(view!=null)view.SetHoverLocation(location,false);return;}
            if(game.TryPlay(location)&&view!=null)view.ClearHover();
        }

        public void OnMouseEnter()
        {
            if(game!=null&&view!=null)
            {
                float startedAt=Time.realtimeSinceStartup;
                bool legal=game.IsLegalMove(location);
                view.RecordHoverLegalQuery((Time.realtimeSinceStartup-startedAt)*1000f);
                view.SetHoverLocation(location,legal);
            }
        }

        public void OnMouseExit()
        {
            if(view!=null&&view.hoverLocation==location)view.ClearHover();
        }
    }
}
