using UdonSharp;

namespace PureUdonGo
{
    /// <summary>
    /// ClientSim-only compiled-Udon proof that one complete KataGo graph is
    /// split across bounded frame slices and that a later in-progress graph
    /// cannot continue after authoritative revision invalidation.
    /// </summary>
    public sealed class GoGpuGraphSlicingProbe : UdonSharpBehaviour
    {
        private const int WAIT_FIRST_GRAPH=1;
        private const int WAIT_SECOND_GRAPH=2;
        private const int VERIFY_CANCEL=3;
        private const int REQUIRED_STAGE_MASK=1022; // NN_INPUT_UPLOAD..NN_PACK_OUTPUT

        public GoGame game;
        public GoAiController controller;
        public GoGpuNeuralRuntime runtime;
        public bool probeFinished;
        public bool probePassed;
        public int phase;
        public int firstGraphFrames;
        public int firstGraphPasses;
        public int firstGraphMaxPassesOneFrame;
        public int firstGraphStageMask;
        public float firstGraphMaxSubmitMilliseconds;
        public float firstGraphSmoothedSubmitMilliseconds;
        public float secondGraphSmoothedSubmitMilliseconds;
        public int cancelledGraphStage;
        public int cancelledGraphProgress;
        public int cancelledGraphPasses;
        public int passesAfterCancellation;
        public int cancelledGpuGraphs;
        public int cancellationFramesObserved;
        public int revisionBeforeCancellation;
        public int revisionAfterCancellation;
        public int searchTokenBeforeCancellation;
        public string failure="";

        public void RunGpuGraphSlicingProbe()
        {
            probeFinished=false;probePassed=false;phase=0;failure="";
            firstGraphFrames=0;firstGraphPasses=0;firstGraphMaxPassesOneFrame=0;
            firstGraphStageMask=0;firstGraphMaxSubmitMilliseconds=0f;
            firstGraphSmoothedSubmitMilliseconds=0f;
            secondGraphSmoothedSubmitMilliseconds=0f;cancelledGraphStage=0;
            cancelledGraphProgress=0;cancelledGraphPasses=0;
            passesAfterCancellation=0;cancelledGpuGraphs=0;
            cancellationFramesObserved=0;revisionBeforeCancellation=0;
            revisionAfterCancellation=0;searchTokenBeforeCancellation=0;
            if(game==null||controller==null||runtime==null||controller.search==null)
            {
                Finish("GPU graph slicing probe references are incomplete");
                return;
            }
            controller.autoStart=false;
            controller.InvalidateSearch();
            game.SetMatchMode(GoAiController.MODE_AIVAI);
            game.RequestForceReset();
            game.SetBlackStarts();
            game.RequestStartMatch();
            controller.autoStart=true;
            phase=WAIT_FIRST_GRAPH;
        }

        public void Update()
        {
            if(probeFinished||phase==0)return;
            if(controller.controllerState==GoAiController.STATE_ERROR)
            {
                Finish("AI controller error: "+controller.lastError);
                return;
            }
            if(phase==WAIT_FIRST_GRAPH)
            {
                if(controller.lastRootOutputRevision<=0||
                    controller.lastRootOutputSearchToken<=0||
                    controller.lastRootReadbackStages!=1)return;
                firstGraphFrames=runtime.gpuGraphFrames;
                firstGraphPasses=runtime.executedPasses;
                firstGraphMaxPassesOneFrame=runtime.maxGpuGraphPassesOneFrame;
                firstGraphStageMask=runtime.gpuGraphVisitedStageMask;
                firstGraphMaxSubmitMilliseconds=runtime.maxGpuSubmitMsOneFrame;
                firstGraphSmoothedSubmitMilliseconds=runtime.smoothedGpuSubmitMilliseconds;
                bool stages=(firstGraphStageMask&REQUIRED_STAGE_MASK)==REQUIRED_STAGE_MASK;
                if(firstGraphFrames<2||
                    firstGraphPasses!=GoGpuNeuralRuntime.FUSED_PASSES_WITH_OWNERSHIP||
                    firstGraphMaxPassesOneFrame<=0||
                    firstGraphMaxPassesOneFrame>=firstGraphPasses||!stages)
                {
                    Finish("graph was not fully frame-sliced frames="+firstGraphFrames+
                        " passes="+firstGraphPasses+" maxPasses="+
                        firstGraphMaxPassesOneFrame+" stageMask="+firstGraphStageMask);
                    return;
                }
                phase=WAIT_SECOND_GRAPH;
                return;
            }
            if(phase==WAIT_SECOND_GRAPH)
            {
                int stage=runtime.gpuGraphStage;
                if(stage<GoGpuNeuralRuntime.NN_INITIAL_CONV||
                    stage>GoGpuNeuralRuntime.NN_PACK_OUTPUT||
                    runtime.executedPasses<=0||
                    runtime.executedPasses>=firstGraphPasses)return;
                secondGraphSmoothedSubmitMilliseconds=runtime.smoothedGpuSubmitMilliseconds;
                controller.autoStart=false;
                cancelledGraphStage=stage;
                cancelledGraphProgress=runtime.gpuGraphStageProgress;
                cancelledGraphPasses=runtime.executedPasses;
                revisionBeforeCancellation=game.revision;
                searchTokenBeforeCancellation=controller.search.searchToken;
                game.RequestForceReset();
                revisionAfterCancellation=game.revision;
                passesAfterCancellation=runtime.executedPasses;
                cancelledGpuGraphs=runtime.cancelledGpuGraphs;
                phase=VERIFY_CANCEL;
                return;
            }
            cancellationFramesObserved++;
            passesAfterCancellation=runtime.executedPasses;
            if(cancellationFramesObserved<4)return;
            bool stopped=runtime.gpuGraphStage==GoGpuNeuralRuntime.NN_IDLE&&
                passesAfterCancellation==cancelledGraphPasses;
            bool staleRejected=revisionAfterCancellation>revisionBeforeCancellation&&
                controller.search.phase==GoMctsSearch.PHASE_CANCELLED&&
                runtime.cancelledGpuGraphs>0;
            if(!stopped||!staleRejected)
            {
                Finish("stale graph continued stopped="+stopped+
                    " staleRejected="+staleRejected+" passes="+
                    cancelledGraphPasses+"->"+passesAfterCancellation+
                    " revision="+revisionBeforeCancellation+"->"+
                    revisionAfterCancellation+" secondGraphEmaMs="+
                    secondGraphSmoothedSubmitMilliseconds);
                return;
            }
            probePassed=true;probeFinished=true;phase=0;
        }

        private void Finish(string message)
        {
            if(controller!=null)controller.autoStart=false;
            failure=message;phase=0;probeFinished=true;
        }
    }
}
