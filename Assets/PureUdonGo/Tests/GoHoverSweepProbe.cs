using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>Single-ClientSim gate for cached legality and delta hover presentation.</summary>
    public sealed class GoHoverSweepProbe : UdonSharpBehaviour
    {
        private const int WARM=1,SWEEP=2,POOL=3,SETTLE=4;
        // The generated owner schedules a recovery at two and thirty frames
        // during ClientSim startup.  Do not take the hover baseline until that
        // known lifecycle work has drained; the sweep itself remains strict.
        private const int STARTUP_RECOVERY_SETTLE_FRAMES=36;
        public GoGame game;
        public GoBoardView view;
        public GoBoardPool pool;
        public GoBoardCell[] cells;
        public GoBoardInput inputReceiver;
        public GoGame[] visibleGames;
        public bool probeFinished;
        public bool probePassed;
        public int phase;
        public int sweepCursor;
        public int warmupFrames;
        public int warmupPoints;
        public int warmupMaxPointsOneFrame;
        public int fullRefreshBeforeSweep;
        public int fullRefreshAfterSweep;
        public int legalityMismatches;
        public int previewIdentityChanges;
        public int poolFrames;
        public int poolMaxPointsOneFrame;
        public string failure="";
        private GameObject previewIdentity;
        private int settleLastRefreshCount;
        private int settleStableFrames;

        public void RunHoverSweepProbe()
        {
            probeFinished=false;probePassed=false;phase=WARM;sweepCursor=0;
            warmupFrames=0;warmupPoints=0;warmupMaxPointsOneFrame=0;
            fullRefreshBeforeSweep=0;fullRefreshAfterSweep=0;legalityMismatches=0;
            previewIdentityChanges=0;poolFrames=0;poolMaxPointsOneFrame=0;failure="";
            previewIdentity=view==null?null:view.previewStone;
            if(game==null||view==null||pool==null||
                (inputReceiver==null&&(cells==null||cells.Length<GoGame.AREA)))
            {Finish("hover sweep references are incomplete");return;}
            pool.enabled=false;
            game.ResetMoveMaskCacheForVerifier();
        }

        public void Update()
        {
            if(probeFinished||phase==0)return;
            if(phase==WARM)
            {
                int advanced=game.StepMoveMaskCacheTimeSliced(
                    GoGame.MOVE_MASK_WARMUP_DEFAULT_MS,24);
                warmupPoints+=advanced;warmupFrames++;
                if(advanced>warmupMaxPointsOneFrame)warmupMaxPointsOneFrame=advanced;
                if(!game.IsMoveMaskCacheCompleteForCurrentRevision())return;
                // Startup/deserialization recovery can legitimately reconcile
                // the presentation after the move-mask reaches 361 points.
                // Wait for that lifecycle work to become stable before taking
                // the strict hover baseline; any refresh during the actual
                // 361-event sweep is still a hard failure below.
                settleLastRefreshCount=view.fullBoardRefreshCount;
                settleStableFrames=0;phase=SETTLE;return;
            }
            if(phase==SETTLE)
            {
                int refreshCount=view.fullBoardRefreshCount;
                if(refreshCount==settleLastRefreshCount)settleStableFrames++;
                else {settleLastRefreshCount=refreshCount;settleStableFrames=0;}
                if(settleStableFrames<STARTUP_RECOVERY_SETTLE_FRAMES)return;
                fullRefreshBeforeSweep=refreshCount;sweepCursor=0;phase=SWEEP;return;
            }
            if(phase==SWEEP)
            {
                int loc=sweepCursor;
                // Use the same exported entry point as each generated UGUI
                // board target. Production's board UIShape collider is a
                // trigger; table geometry has no solid physics collider.
                if(inputReceiver!=null)inputReceiver.SendCustomEvent("Hover"+loc.ToString("000"));
                else cells[loc].OnMouseEnter();
                if(view.hoverLocation!=loc)legalityMismatches++;
                if(view.hoverLegal!=game.IsLegalMove(loc))legalityMismatches++;
                if(inputReceiver!=null)inputReceiver.SendCustomEvent("Exit"+loc.ToString("000"));
                else cells[loc].OnMouseExit();
                if(view.hoverLocation!=GoGame.NONE)legalityMismatches++;
                if(view.previewStone!=previewIdentity)previewIdentityChanges++;
                sweepCursor++;
                if(sweepCursor<GoGame.AREA)return;
                fullRefreshAfterSweep=view.fullBoardRefreshCount;
                if(fullRefreshAfterSweep!=fullRefreshBeforeSweep||legalityMismatches!=0||previewIdentityChanges!=0)
                {Finish("hover sweep changed full-board presentation refresh="+(fullRefreshAfterSweep-fullRefreshBeforeSweep)+" legalMismatch="+legalityMismatches+" previewChanges="+previewIdentityChanges+" lastReason="+view.lastFullRefreshReason+" lastFrame="+view.lastFullRefreshFrame+" lastRevision="+view.lastFullRefreshRevision);return;}
                for(int i=0;i<visibleGames.Length;i++)if(visibleGames[i]!=null)visibleGames[i].ResetMoveMaskCacheForVerifier();
                pool.enabled=true;
                phase=POOL;return;
            }
            poolFrames++;int points=pool.moveMaskWarmupPointsThisFrame;
            if(points>poolMaxPointsOneFrame)poolMaxPointsOneFrame=points;
            if(points>Mathf.Clamp(pool.moveMaskWarmupQuota,1,24))
            {Finish("room move-mask quota exceeded points="+points);return;}
            bool complete=true;
            for(int i=0;i<visibleGames.Length;i++)if(visibleGames[i]!=null&&!visibleGames[i].IsMoveMaskCacheCompleteForCurrentRevision())complete=false;
            if(!complete)return;
            if(warmupPoints<GoGame.AREA||warmupFrames<=1||warmupMaxPointsOneFrame>24||poolMaxPointsOneFrame>24)
            {Finish("room warm-up did not complete cooperatively");return;}
            probePassed=true;probeFinished=true;phase=0;
        }

        private void Finish(string message){if(pool!=null)pool.enabled=true;failure=message;probePassed=false;probeFinished=true;phase=0;}
    }
}
