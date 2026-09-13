using UdonSharp;
using UnityEngine;
using VRC.Udon;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Rendering;
#else
using UnityEngine.Rendering;
#endif

namespace PureUdonGo
{
    /// <summary>
    /// Final-output reader for the GPU graph. Intermediate activations remain
    /// on the GPU. A VRChat SDK build uses VRCAsyncGPUReadback and its Udon
    /// event; the non-SDK branch is a Unity editor validation fallback.
    /// Every evaluation is coalesced into one packed target and one async
    /// request. Root evaluations include ownership in the packed spatial
    /// channel; leaf evaluations keep the same layout while PUCT consumes only
    /// policy/value/score. Reader identity remains stable across quarantine.
    /// </summary>
    public sealed class GoGpuNeuralOutputReader : UdonSharpBehaviour
    {
        public const int READBACK_IDLE=0;
        public const int READBACK_WAITING=1;
        public const int READBACK_COMPLETE=2;
        public const int READBACK_ERROR=3;
        public const int READBACK_CANCELLED=4;
        // A cancelled request cannot be cancelled at the underlying GPU API.
        // Keep the channel quarantined until its callback is observed, so a
        // late callback can never be mistaken for a newer request.
        public const int READBACK_QUARANTINED=5;
        public const int READBACK_RECOVERED=6;
        private const int STAGE_PACKED=0;
        private const int STAGE_COUNT=1;

        public GoGpuNeuralRuntime runtime;
        // The reader shares a Gameplay GameObject with several other Udon
        // behaviours. The generator binds this to this proxy's exact backing
        // UdonBehaviour; GetComponent<UdonBehaviour>() would select the wrong
        // receiver in that multi-program object.
        public UdonBehaviour eventReceiver;
        public float[] policySpatial=new float[GoGame.AREA];
        public float[] policyPass=new float[1];
        public float[] value=new float[3];
        public float[] score=new float[4];
        public float[] ownership=new float[GoGame.AREA];
        public int readbackState=READBACK_IDLE;
        public int readbackStage=-1;
        public int completedStages;
        public bool ownershipReadbackIncluded;
        public int ownershipReadbackCount;
        public int staleCallbacks;
        public int abandonedReadbacks;
        public int quarantineCount;
        public bool quarantinePending;
        public int recoveredCallbacks;
        public int requestGeneration;
        public int resultToken;
        public int resultNode=-1;
        public int resultRevision;
        public int resultSettingsRevision;
        public int resultOwnerLifecycle;
        public int resultHash0;
        public int resultHash1;
        public int resultHash2;
        public int resultHash3;
        public float lastAsyncReadbackMilliseconds;
        public int lastAsyncReadbackRequests;
        public string lastError="";

        private Texture2D packedReadback;
        private Color[] packedPixels=new Color[GoGame.SIZE*(GoGame.SIZE+1)];
        // Pixel zero carries policy-pass/value; pixel one carries score.
        private int discardedCallbacks;
        private float asyncReadbackStartedAt;
        private bool includeOwnership;

#if !VRC_SDK_VRCSDK3
        private AsyncGPUReadbackRequest unityRequest;
#endif

        public bool BeginAsyncReadback(GoGpuNeuralRuntime source,int token,int node,int revision,int settingsRevision,int ownerLifecycle,int hash0,int hash1,int hash2,int hash3)
        {
            return BeginAsyncReadback(source,token,node,revision,settingsRevision,
                ownerLifecycle,hash0,hash1,hash2,hash3,true);
        }

        public bool BeginAsyncReadback(GoGpuNeuralRuntime source,int token,int node,int revision,int settingsRevision,int ownerLifecycle,int hash0,int hash1,int hash2,int hash3,bool readOwnership)
        {
            lastError="";
            if(source==null){lastError="GPU runtime is not assigned";readbackState=READBACK_ERROR;return false;}
            if(readbackState==READBACK_WAITING){lastError="a GPU readback is already pending";return false;}
            if(discardedCallbacks>0){quarantinePending=true;readbackState=READBACK_QUARANTINED;lastError="a cancelled GPU readback callback is still pending";return false;}
            if(!HasOutputs(source)){lastError="GPU runtime packed output has invalid dimensions";readbackState=READBACK_ERROR;return false;}
            runtime=source;resultToken=token;resultNode=node;resultRevision=revision;resultSettingsRevision=settingsRevision;resultOwnerLifecycle=ownerLifecycle;resultHash0=hash0;resultHash1=hash1;resultHash2=hash2;resultHash3=hash3;
            includeOwnership=readOwnership;
            ownershipReadbackIncluded=readOwnership;
            if(readOwnership)ownershipReadbackCount++;
            requestGeneration++;if(requestGeneration==0)requestGeneration=1;
            completedStages=0;readbackStage=STAGE_PACKED;readbackState=READBACK_WAITING;
            lastAsyncReadbackMilliseconds=0f;lastAsyncReadbackRequests=0;
            asyncReadbackStartedAt=Time.realtimeSinceStartup;
            RequestCurrentStage();
            return readbackState==READBACK_WAITING;
        }

        public bool CanBeginAsyncReadback(){return readbackState!=READBACK_WAITING&&discardedCallbacks==0;}

        /// <summary>
        /// Allocates only the editor fallback staging texture. VRChat uses
        /// the preallocated packed pixel array and does not start a request.
        /// </summary>
        public bool PrepareRuntimeResources()
        {
#if !VRC_SDK_VRCSDK3
            EnsureTextures();
#endif
            return true;
        }

        public void CancelAsyncReadback()
        {
            if(readbackState==READBACK_WAITING)
            {
                discardedCallbacks++;
                abandonedReadbacks++;
                quarantineCount=discardedCallbacks;
                quarantinePending=true;
                readbackState=READBACK_QUARANTINED;
            }
            else if(discardedCallbacks>0)
            {
                quarantineCount=discardedCallbacks;
                quarantinePending=true;
                readbackState=READBACK_QUARANTINED;
            }
            else
            {
                quarantinePending=false;
                readbackState=READBACK_CANCELLED;
            }
            requestGeneration++;if(requestGeneration==0)requestGeneration=1;
            readbackStage=-1;
        }

        public bool TrySubmitToSearch(GoMctsSearch search)
        {
            if(readbackState!=READBACK_COMPLETE){lastError="GPU readback is not complete";return false;}
            if(search==null){lastError="MCTS search is not assigned";return false;}
            float scoreMean=DecodeScoreMean();
            float scoreStdev=DecodeScoreStdev();
            bool accepted=resultNode>=0
                ?search.SubmitNeuralLogitsForNodeWithScore(resultNode,resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatial,policyPass[0],value[0],value[1],value[2],scoreMean,scoreStdev)
                :search.SubmitNeuralLogitsWithScore(resultToken,resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,resultHash2,resultHash3,policySpatial,policyPass[0],value[0],value[1],value[2],scoreMean,scoreStdev);
            if(!accepted)lastError="MCTS rejected GPU result as stale or invalid";
            return accepted;
        }

        /// <summary>
        /// Transfers a completed result into the MCTS expansion state without
        /// executing the full legal-edge normalization pass in this frame.
        /// The controller must advance GoMctsSearch.StepPendingExpansion.
        /// </summary>
        public bool TryBeginSubmitToSearch(GoMctsSearch search)
        {
            if(readbackState!=READBACK_COMPLETE){lastError="GPU readback is not complete";return false;}
            if(search==null){lastError="MCTS search is not assigned";return false;}
            float scoreMean=DecodeScoreMean();
            float scoreStdev=DecodeScoreStdev();
            bool accepted=search.BeginNeuralLogitsForNodeWithScore(resultNode,resultToken,
                resultRevision,resultSettingsRevision,resultOwnerLifecycle,resultHash0,resultHash1,
                resultHash2,resultHash3,policySpatial,policyPass[0],value[0],value[1],value[2],
                scoreMean,scoreStdev);
            if(!accepted)lastError="MCTS rejected GPU result as stale or invalid";
            return accepted;
        }

        public float DecodeScoreMean()
        {
            return score==null||score.Length<1?0f:score[0]*GoProductionModelLayout.ScoreMeanMultiplier;
        }

        public float DecodeScoreStdev()
        {
            if(score==null||score.Length<2)return 0f;
            return Mathf.Log(1f+Mathf.Exp(Mathf.Clamp(score[1],-80f,80f)))*GoProductionModelLayout.ScoreStdevMultiplier;
        }

        public float DecodeNoResultProbability()
        {
            if(value==null||value.Length<3)return 0f;
            float maximum=Mathf.Max(value[0],Mathf.Max(value[1],value[2]));
            float win=Mathf.Exp(Mathf.Clamp(value[0]-maximum,-80f,80f));
            float loss=Mathf.Exp(Mathf.Clamp(value[1]-maximum,-80f,80f));
            float noResult=Mathf.Exp(Mathf.Clamp(value[2]-maximum,-80f,80f));
            float total=win+loss+noResult;
            return total>0f?noResult/total:0.33333334f;
        }

        public float DecodeUnconditionalScoreMean()
        {
            return DecodeScoreMean()*(1f-Mathf.Clamp01(DecodeNoResultProbability()));
        }

        public float DecodeUnconditionalScoreStdev()
        {
            float conditionalMean=DecodeScoreMean();
            float conditionalStdev=DecodeScoreStdev();
            float resultWeight=1f-Mathf.Clamp01(DecodeNoResultProbability());
            float effectiveMean=conditionalMean*resultWeight;
            float secondMoment=(conditionalMean*conditionalMean+conditionalStdev*conditionalStdev)*resultWeight;
            return Mathf.Sqrt(Mathf.Max(0f,secondMoment-effectiveMean*effectiveMean));
        }

        public float DecodeUnconditionalLead()
        {
            if(score==null||score.Length<3)return 0f;
            return score[2]*GoProductionModelLayout.LeadMultiplier*(1f-Mathf.Clamp01(DecodeNoResultProbability()));
        }

#if !VRC_SDK_VRCSDK3
        public bool ReadNow()
        {
            lastError="";
            if(readbackState==READBACK_WAITING){lastError="cannot synchronously read while async readback is pending";return false;}
            if(discardedCallbacks>0){lastError="cannot read while a cancelled GPU callback is quarantined";return false;}
            if(runtime==null){lastError="GPU runtime is not assigned";return false;}
            if(!HasOutputs(runtime)){lastError="GPU runtime packed output has invalid dimensions";return false;}
            EnsureTextures();
            includeOwnership=true;ownershipReadbackIncluded=true;ownershipReadbackCount++;
            ReadPackedOutput(runtime.packedReadbackOutputs);
            completedStages=STAGE_COUNT;readbackStage=-1;readbackState=READBACK_COMPLETE;
            return true;
        }
#endif

        public void ReleaseResources()
        {
            if(readbackState==READBACK_WAITING)CancelAsyncReadback();
            if(packedReadback!=null){Object.Destroy(packedReadback);packedReadback=null;}
            readbackStage=-1;resultNode=-1;
            if(discardedCallbacks>0)
            {
                quarantineCount=discardedCallbacks;
                quarantinePending=true;
                readbackState=READBACK_QUARANTINED;
            }
            else
            {
                quarantinePending=false;
                readbackState=READBACK_IDLE;
            }
        }

#if VRC_SDK_VRCSDK3
        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
        {
            if(discardedCallbacks>0)
            {
                ObserveDiscardedCallback();
                return;
            }
            if(readbackState!=READBACK_WAITING){staleCallbacks++;return;}
            if(request.hasError){FailReadback("VRCAsyncGPUReadback reported an error");return;}
            if(!CopyVrcData(request)){FailReadback("VRCAsyncGPUReadback data shape/type was not accepted");return;}
            AdvanceStage();
        }
#else
        private void OnUnityAsyncGpuReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if(discardedCallbacks>0)
            {
                ObserveDiscardedCallback();
                return;
            }
            if(readbackState!=READBACK_WAITING){staleCallbacks++;return;}
            if(request.hasError){FailReadback("Unity AsyncGPUReadback reported an error");return;}
            var data=request.GetData<Color>();
            if(data.Length<packedPixels.Length){FailReadback("Unity AsyncGPUReadback returned too little data");return;}
            for(int i=0;i<packedPixels.Length;i++)packedPixels[i]=data[i];
            AdvanceStage();
        }
#endif

        // The dedicated ClientSim adversarial verifier calls this Udon event
        // after creating a real quarantined request. It shares the exact state
        // transition used by the platform callback and never fabricates neural
        // output data.
        public void ObserveDiscardedCallbackForVerifier()
        {
            if(discardedCallbacks>0)ObserveDiscardedCallback();
        }

        private void ObserveDiscardedCallback()
        {
            discardedCallbacks--;quarantineCount=discardedCallbacks;staleCallbacks++;
            if(discardedCallbacks==0)
            {
                quarantinePending=false;readbackState=READBACK_RECOVERED;
                readbackStage=-1;resultNode=-1;recoveredCallbacks++;
            }
        }

        private bool HasOutputs(GoGpuNeuralRuntime source)
        {
            return source!=null&&source.packedReadbackOutputs!=null&&
                source.packedReadbackOutputs.width==GoGame.SIZE&&
                source.packedReadbackOutputs.height==GoGame.SIZE+1;
        }

        private void RequestCurrentStage()
        {
            RenderTexture source=CurrentSource();
            if(source==null){FailReadback("GPU output for current readback stage is null");return;}
            lastAsyncReadbackRequests++;
#if VRC_SDK_VRCSDK3
            if(eventReceiver==null){FailReadback("Udon event receiver component is missing");return;}
            VRCAsyncGPUReadback.Request(source,0,TextureFormat.RGBAFloat,eventReceiver);
#else
            unityRequest=AsyncGPUReadback.Request(source,0,OnUnityAsyncGpuReadbackComplete);
#endif
        }

        private RenderTexture CurrentSource()
        {
            if(runtime==null)return null;
            if(readbackStage==STAGE_PACKED)return runtime.packedReadbackOutputs;
            return null;
        }

#if VRC_SDK_VRCSDK3
        private bool CopyVrcData(VRCAsyncGPUReadbackRequest request)
        {
            return request.TryGetData(packedPixels);
        }
#endif

        private void AdvanceStage()
        {
            for(int i=0;i<GoGame.AREA;i++)
            {
                policySpatial[i]=packedPixels[i].r;
                if(includeOwnership)ownership[i]=packedPixels[i].g;
            }
            Color policyValue=packedPixels[GoGame.AREA];
            policyPass[0]=policyValue.r;
            value[0]=policyValue.g;value[1]=policyValue.b;value[2]=policyValue.a;
            Color scoreValue=packedPixels[GoGame.AREA+1];
            score[0]=scoreValue.r;score[1]=scoreValue.g;
            score[2]=scoreValue.b;score[3]=scoreValue.a;
            completedStages++;readbackStage++;
            if(readbackStage>=STAGE_COUNT)
            {
                lastAsyncReadbackMilliseconds=(Time.realtimeSinceStartup-asyncReadbackStartedAt)*1000f;
                readbackStage=-1;readbackState=READBACK_COMPLETE;return;
            }
            RequestCurrentStage();
        }

        private void FailReadback(string message){lastError=message;readbackStage=-1;readbackState=READBACK_ERROR;}

#if !VRC_SDK_VRCSDK3
        private void EnsureTextures()
        {
            if(packedReadback==null)packedReadback=new Texture2D(GoGame.SIZE,GoGame.SIZE+1,TextureFormat.RGBAFloat,false,true);
        }

        private void ReadPackedOutput(RenderTexture source)
        {
            RenderTexture old=RenderTexture.active;RenderTexture.active=source;
            packedReadback.ReadPixels(new Rect(0,0,GoGame.SIZE,GoGame.SIZE+1),0,0,false);
            packedReadback.Apply(false,false);RenderTexture.active=old;
            for(int y=0;y<GoGame.SIZE;y++)for(int x=0;x<GoGame.SIZE;x++)
            {
                Color packed=packedReadback.GetPixel(x,y);int loc=y*GoGame.SIZE+x;
                policySpatial[loc]=packed.r;ownership[loc]=packed.g;
            }
            Color policyValue=packedReadback.GetPixel(0,GoGame.SIZE);
            policyPass[0]=policyValue.r;
            value[0]=policyValue.g;value[1]=policyValue.b;value[2]=policyValue.a;
            Color scoreValue=packedReadback.GetPixel(1,GoGame.SIZE);
            score[0]=scoreValue.r;score[1]=scoreValue.g;
            score[2]=scoreValue.b;score[3]=scoreValue.a;
        }
#endif
    }
}
