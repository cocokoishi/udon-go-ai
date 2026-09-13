using UdonSharp;
using UnityEngine;

namespace PureUdonGo
{
    /// <summary>
    /// Central visual update for the 19x19 board. Stones are pre-created by
    /// the editor generator; this component only toggles them and updates the
    /// last-move, ko and hover markers when GoGame refreshes.
    /// </summary>
    public sealed class GoBoardView : UdonSharpBehaviour
    {
        public const int REFRESH_OTHER=0;
        public const int REFRESH_INITIAL=1;
        public const int REFRESH_MOVE=2;
        public const int REFRESH_CAPTURE=3;
        public const int REFRESH_UNDO=4;
        public const int REFRESH_RESET=5;
        public const int REFRESH_DESERIALIZE=6;
        public const int REFRESH_HANDICAP=7;
        public const int REFRESH_RECOVERY=8;
        public GoGame game;
        public GoBoardInput inputReceiver;
        public GameObject[] stoneObjects=new GameObject[GoGame.AREA];
        public Renderer[] stoneRenderers=new Renderer[GoGame.AREA];
        public Material blackStoneMaterial;
        public Material whiteStoneMaterial;
        public GameObject lastMoveMarker;
        public GameObject koMarker;
        public GameObject hoverMarker;
        public GameObject hintMarker;
        // One pooled ghost stone is teleported to the current intersection;
        // it starts below the table and is never instantiated during hover.
        public GameObject previewStone;
        public Renderer previewStoneRenderer;
        public float previewStoneCenterY=0.171f;
        public Vector3 previewStoragePosition=new Vector3(0f,-0.30f,0f);
        public Renderer hoverRenderer;
        public Material legalHoverMaterial;
        public Material illegalHoverMaterial;
        public float gridSpacing=0.32f;
        // Generated boards place the point/illegal-hover marker flush with
        // the stone crown. Keep the same safe default for non-generated
        // standalone fixtures.
        public float markerHeight=0.256f;
        // Hover/illegal feedback is drawn on the board surface itself. Keep
        // last-move/AI-hint markers on the stone crown, but never leave the
        // red forbidden-point indicator floating above an empty intersection.
        public float hoverMarkerHeight=0.098f;
        public float koMarkerHeight=0.098f;
        // Generated production tables let GoBoardPool own the room-wide
        // legality warm-up quota. Standalone verifier fixtures can leave this
        // false and retain the per-view fallback below.
        public bool moveMaskWarmupManagedByPool;
        public int hoverLocation=GoGame.NONE;
        public bool hoverLegal;
        [Header("Local capture presentation")]
        public float captureAnimationDuration=0.22f;
        public float captureSinkDistance=0.018f;
        public int lastRefreshReason=REFRESH_OTHER;
        public int captureAnimationStartCount;
        public int lastCaptureAnimationStoneCount;
        // Hover profiler counters are local diagnostics. They make it
        // possible to verify that pointer motion uses the fast delta path
        // instead of causing a full 361-stone refresh.
        [System.NonSerialized] public int hoverEvents;
        [System.NonSerialized] public int fullBoardRefreshCount;
        [System.NonSerialized] public int hoverFastUpdateCount;
        [System.NonSerialized] public float lastHoverPresentationMilliseconds;
        [System.NonSerialized] public float maxHoverPresentationMilliseconds;
        [System.NonSerialized] public int hoverLegalQueryCount;
        [System.NonSerialized] public float lastHoverLegalQueryMilliseconds;
        [System.NonSerialized] public float maxHoverLegalQueryMilliseconds;
        // The hover gate uses these only to identify an unexpected lifecycle
        // refresh that lands during the sweep; they do not participate in
        // presentation or rule semantics.
        [System.NonSerialized] public int lastFullRefreshReason;
        [System.NonSerialized] public int lastFullRefreshFrame;
        [System.NonSerialized] public int lastFullRefreshRevision;

        private int[] renderedBoard=new int[GoGame.AREA];
        private bool hasRenderedBoard;
        private bool[] captureAnimating=new bool[GoGame.AREA];
        private Vector3[] captureBaseScale=new Vector3[GoGame.AREA];
        private float[] captureBaseY=new float[GoGame.AREA];
        private int[] captureLocations=new int[GoGame.AREA];
        private int captureCount;
        private float captureStartedAt;
        private bool captureAnimationActive;
        private int lastRenderedAudioEventRevision;

        private int activeRefreshReason=REFRESH_OTHER;

        public void Start(){RefreshForReason(REFRESH_INITIAL);}

        public void Update()
        {
            // Warm the position's legal/superko mask independently of pointer
            // motion. A bounded quota keeps the first hover after a move from
            // becoming 361 synchronous rule probes while retaining the exact
            // GoGame result for clicks and AI hints.
            if(!moveMaskWarmupManagedByPool&&game!=null&&
                !game.IsMoveMaskCacheCompleteForCurrentRevision())
                game.StepMoveMaskCacheTimeSliced(GoGame.MOVE_MASK_WARMUP_DEFAULT_MS,24);
            if(!captureAnimationActive)return;
            float duration=Mathf.Clamp(captureAnimationDuration,0.18f,0.30f);
            float progress=Mathf.Clamp01((Time.time-captureStartedAt)/duration);
            for(int i=0;i<captureCount;i++)
            {
                int loc=captureLocations[i];
                if(!captureAnimating[loc])continue;
                GameObject stone=stoneObjects[loc];
                if(stone==null)continue;
                Transform stoneTransform=stone.transform;
                Vector3 scale=captureBaseScale[loc];
                float remaining=1f-progress;
                scale.x*=remaining;scale.y*=remaining;scale.z*=remaining;
                stoneTransform.localScale=scale;
                Vector3 position=stoneTransform.localPosition;
                position.y=captureBaseY[loc]-captureSinkDistance*progress;
                stoneTransform.localPosition=position;
            }
            if(progress<1f)return;
            for(int i=0;i<captureCount;i++)
            {
                int loc=captureLocations[i];
                if(!captureAnimating[loc])continue;
                GameObject stone=stoneObjects[loc];
                if(stone!=null)
                {
                    stone.transform.localScale=captureBaseScale[loc];
                    Vector3 position=stone.transform.localPosition;
                    position.y=captureBaseY[loc];
                    stone.transform.localPosition=position;
                    stone.SetActive(false);
                }
                if(stoneRenderers!=null&&loc<stoneRenderers.Length&&stoneRenderers[loc]!=null)
                    stoneRenderers[loc].enabled=false;
                captureAnimating[loc]=false;
            }
            captureCount=0;captureAnimationActive=false;
        }

        public void RefreshNow()
        {
            if(game==null)return;
            fullBoardRefreshCount++;
            lastFullRefreshReason=activeRefreshReason;
            lastFullRefreshFrame=Time.frameCount;
            lastFullRefreshRevision=game.revision;
            bool initialRender=!hasRenderedBoard;
            bool boardChanged=false;
            if(!hasRenderedBoard)
            {
                for(int i=0;i<GoGame.AREA;i++)renderedBoard[i]=game.board[i];
                hasRenderedBoard=true;
            }
            else
            {
                for(int i=0;i<GoGame.AREA;i++)
                    if(renderedBoard[i]!=game.board[i]){boardChanged=true;break;}
            }
            // A disappearing stone is not necessarily a capture: reset, undo,
            // recovery and late-join snapshots also replace the board.  Only
            // the authoritative capture event for this revision may trigger
            // the presentation animation.
            bool captureEvent=activeRefreshReason==REFRESH_CAPTURE&&
                game.audioEventType==GoGame.AUDIO_CAPTURE&&
                game.lastAudioEventRevision!=lastRenderedAudioEventRevision;
            if(boardChanged&&captureEvent)PrepareCaptureAnimations();
            for(int loc=0;loc<GoGame.AREA;loc++)
            {
                int value=game.board[loc];
                // Hints/offers/seat refreshes must not reassign 361 materials
                // and active states. Capture cancellation clears hasRenderedBoard
                // below so interrupted animation is reconciled on refresh.
                if(!initialRender&&renderedBoard[loc]==value&&!captureAnimating[loc])continue;
                bool occupied=value!=GoGame.EMPTY;
                if(stoneObjects!=null&&loc<stoneObjects.Length&&stoneObjects[loc]!=null)
                {
                    if(occupied)
                    {
                        if(captureAnimating[loc])
                        {
                            captureAnimating[loc]=false;
                            stoneObjects[loc].transform.localScale=captureBaseScale[loc];
                            Vector3 position=stoneObjects[loc].transform.localPosition;
                            position.y=captureBaseY[loc];
                            stoneObjects[loc].transform.localPosition=position;
                        }
                        stoneObjects[loc].SetActive(true);
                    }
                    else if(!captureAnimating[loc])stoneObjects[loc].SetActive(false);
                }
                if(stoneRenderers!=null&&loc<stoneRenderers.Length&&stoneRenderers[loc]!=null)
                {
                    stoneRenderers[loc].enabled=occupied||captureAnimating[loc];
                    if(occupied)stoneRenderers[loc].sharedMaterial=value==GoGame.BLACK?blackStoneMaterial:whiteStoneMaterial;
                }
                renderedBoard[loc]=value;
            }
            lastRenderedAudioEventRevision=game.lastAudioEventRevision;
            SetMarker(lastMoveMarker,game.lastMove>=0&&game.lastMove<GoGame.AREA,game.lastMove);
            SetMarker(koMarker,game.koLoc>=0&&game.koLoc<GoGame.AREA,game.koLoc,koMarkerHeight);
            SetMarker(hintMarker,game.hintMove>=0&&game.hintMove<GoGame.AREA,game.hintMove);
            UpdateHoverPresentation();
        }

        /// <summary>
        /// Updates only the pointer-dependent visuals.  Hovering from one
        /// intersection to another must not rescan or rewrite all 361 stones;
        /// authoritative board changes still enter through RefreshNow().
        /// </summary>
        private void UpdateHoverPresentation()
        {
            SetMarker(hoverMarker,hoverLocation>=0&&hoverLocation<GoGame.AREA,hoverLocation,hoverMarkerHeight);
            if(hoverRenderer!=null)
                hoverRenderer.sharedMaterial=hoverLegal?legalHoverMaterial:illegalHoverMaterial;
            bool showPreview=hoverLocation>=0&&hoverLocation<GoGame.AREA&&
                hoverLegal&&game.board[hoverLocation]==GoGame.EMPTY&&
                game.gameState==GoGame.STATE_PLAYING&&!game.IsAIControlled(game.sideToMove);
            if(previewStone!=null)
            {
                if(showPreview)
                {
                    previewStone.transform.localPosition=LocationToLocal(hoverLocation,previewStoneCenterY);
                    if(previewStoneRenderer!=null)
                        previewStoneRenderer.sharedMaterial=game.sideToMove==GoGame.BLACK?blackStoneMaterial:whiteStoneMaterial;
                    previewStone.SetActive(true);
                }
                else
                {
                    previewStone.transform.localPosition=previewStoragePosition;
                    previewStone.SetActive(false);
                }
            }
        }

        public void RefreshForReason(int reason)
        {
            if(reason!=REFRESH_CAPTURE)CancelCapturePresentation();
            // A real position/lifecycle change makes any pointer hover stale.
            // Clear only the two hover fields here; RefreshNow will update the
            // pooled preview/marker without routing through another full board
            // refresh or allocating a temporary stone.
            if(reason==REFRESH_MOVE||reason==REFRESH_CAPTURE||
                reason==REFRESH_UNDO||reason==REFRESH_RESET||
                reason==REFRESH_DESERIALIZE||reason==REFRESH_HANDICAP||
                reason==REFRESH_RECOVERY||reason==REFRESH_INITIAL)
            {
                hoverLocation=GoGame.NONE;
                hoverLegal=false;
            }
            activeRefreshReason=reason;
            lastRefreshReason=reason;
            RefreshNow();
            activeRefreshReason=REFRESH_OTHER;
        }

        private void CancelCapturePresentation()
        {
            if(!captureAnimationActive)return;
            for(int i=0;i<captureCount;i++)
            {
                int loc=captureLocations[i];
                GameObject stone=stoneObjects!=null&&loc>=0&&loc<stoneObjects.Length?
                    stoneObjects[loc]:null;
                if(stone!=null)
                {
                    stone.transform.localScale=captureBaseScale[loc];
                    Vector3 position=stone.transform.localPosition;
                    position.y=captureBaseY[loc];stone.transform.localPosition=position;
                }
                captureAnimating[loc]=false;
            }
            captureCount=0;captureAnimationActive=false;
            hasRenderedBoard=false;
        }

        private void PrepareCaptureAnimations()
        {
            for(int i=0;i<GoGame.AREA;i++)
                if(game.board[i]!=GoGame.EMPTY)captureAnimating[i]=false;
            for(int i=0;i<GoGame.AREA;i++)
            {
                if(renderedBoard[i]==GoGame.EMPTY||game.board[i]!=GoGame.EMPTY||
                    stoneObjects==null||i>=stoneObjects.Length||stoneObjects[i]==null||
                    !stoneObjects[i].activeSelf)continue;
                captureAnimating[i]=true;
                captureBaseScale[i]=stoneObjects[i].transform.localScale;
                captureBaseY[i]=stoneObjects[i].transform.localPosition.y;
            }
            captureCount=0;
            for(int i=0;i<GoGame.AREA;i++)
                if(captureAnimating[i])captureLocations[captureCount++]=i;
            if(captureCount>0)
            {
                captureAnimationStartCount++;
                lastCaptureAnimationStoneCount=captureCount;
                captureStartedAt=Time.time;
                captureAnimationActive=true;
            }
        }

        public bool IsCaptureAnimating(int loc)
        {
            return loc>=0&&loc<GoGame.AREA&&captureAnimating[loc];
        }

        public void SetHoverLocation(int loc,bool legal)
        {
            if(game==null)return;
            if(loc<0||loc>=GoGame.AREA)
            {
                ClearHover();
                return;
            }
            hoverEvents++;
            hoverLocation=loc;
            hoverLegal=legal;
            float startedAt=Time.realtimeSinceStartup;
            UpdateHoverPresentation();
            RecordHoverPresentationMilliseconds((Time.realtimeSinceStartup-startedAt)*1000f);
        }

        public void ClearHover()
        {
            hoverEvents++;
            hoverLocation=GoGame.NONE;
            hoverLegal=false;
            if(game!=null)
            {
                float startedAt=Time.realtimeSinceStartup;
                UpdateHoverPresentation();
                RecordHoverPresentationMilliseconds((Time.realtimeSinceStartup-startedAt)*1000f);
            }
        }

        public void RecordHoverLegalQuery(float milliseconds)
        {
            hoverLegalQueryCount++;
            lastHoverLegalQueryMilliseconds=milliseconds;
            if(milliseconds>maxHoverLegalQueryMilliseconds)
                maxHoverLegalQueryMilliseconds=milliseconds;
        }

        private void RecordHoverPresentationMilliseconds(float milliseconds)
        {
            hoverFastUpdateCount++;
            lastHoverPresentationMilliseconds=milliseconds;
            if(milliseconds>maxHoverPresentationMilliseconds)
                maxHoverPresentationMilliseconds=milliseconds;
        }

        private void SetMarker(GameObject marker,bool visible,int loc)
        {
            if(marker==null)return;
            marker.SetActive(visible);
            if(visible)marker.transform.localPosition=LocationToLocal(loc);
        }

        private void SetMarker(GameObject marker,bool visible,int loc,float height)
        {
            if(marker==null)return;
            marker.SetActive(visible);
            if(visible)marker.transform.localPosition=LocationToLocal(loc,height);
        }

        private Vector3 LocationToLocal(int loc)
        {
            return LocationToLocal(loc,markerHeight);
        }

        private Vector3 LocationToLocal(int loc,float height)
        {
            int x=loc%GoGame.SIZE;int y=loc/GoGame.SIZE;
            return new Vector3((x-9)*gridSpacing,height,(y-9)*gridSpacing);
        }
    }
}
