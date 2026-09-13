using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PureUdonGo
{
    /// <summary>
    /// Low-level GPU layer dispatcher. Activations remain in ARGBFloat
    /// RenderTextures; the caller controls when an output is read back.
    /// </summary>
    public sealed class GoGpuLayerExecutor : UdonSharpBehaviour
    {
        public const int BOARD_SIZE=19;
        public Texture2D weightTexture;
        public Material layerMaterial;
        public int weightWidth=1024;
        public int weightHeight=733;
        public string lastError="";

        public RenderTexture CreateActivation(int channels)
        {
            if(channels<=0){lastError="activation channels must be positive";return null;}
            int groups=(channels+3)/4;
            var texture=new RenderTexture(BOARD_SIZE,BOARD_SIZE*groups,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            texture.name="PureUdonGo.Activation."+channels;
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.useMipMap=false;
            texture.autoGenerateMips=false;
            texture.Create();
            return texture;
        }

        public bool DispatchConvolution(RenderTexture input,RenderTexture output,int inputChannels,int outputChannels,int kernelX,int kernelY,int weightBase)
        {
            if(!Validate(input,output,inputChannels,outputChannels))return false;
            if(kernelX<=0||kernelY<=0||(kernelX&1)==0||(kernelY&1)==0){lastError="convolution kernels must be positive odd sizes";return false;}
            Configure(0,input,output,inputChannels,outputChannels,kernelX,kernelY,weightBase,0,0,0);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public bool DispatchAffine(RenderTexture input,RenderTexture output,int channels,int scaleBase,int biasBase,bool relu)
        {
            if(!Validate(input,output,channels,channels))return false;
            Configure(1,input,output,channels,channels,1,1,0,scaleBase,biasBase,relu?1:0);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        // Fusion candidates keep the original convolution accumulation order,
        // then execute its sole consumer. They do not fold model weights.
        public bool DispatchConvolutionBatchNorm(RenderTexture input,RenderTexture output,
            int inputChannels,int outputChannels,int kernelX,int kernelY,int weightBase,
            int meanBase,int varianceBase,int scaleBase,int biasBase,bool hasScale)
        {
            if(!Validate(input,output,inputChannels,outputChannels))return false;
            if(kernelX<=0||kernelY<=0||(kernelX&1)==0||(kernelY&1)==0)
            {lastError="convolution kernels must be positive odd sizes";return false;}
            Configure(12,input,output,inputChannels,outputChannels,kernelX,kernelY,weightBase,scaleBase,biasBase,1);
            layerMaterial.SetInt("_MeanBase",meanBase);
            layerMaterial.SetInt("_VarianceBase",varianceBase);
            layerMaterial.SetInt("_HasScale",hasScale?1:0);
            layerMaterial.SetFloat("_Epsilon",GoProductionModelLayout.BatchNormEpsilon);
            VRCGraphics.Blit(input,output,layerMaterial,0);return true;
        }

        public bool DispatchConvolutionResidual(RenderTexture input,RenderTexture residual,
            RenderTexture output,int inputChannels,int outputChannels,int kernelX,int kernelY,int weightBase)
        {
            if(!Validate(input,output,inputChannels,outputChannels))return false;
            if(residual==null||residual.width!=output.width||residual.height!=output.height||
                kernelX<=0||kernelY<=0||(kernelX&1)==0||(kernelY&1)==0)
            {lastError="fused convolution residual dimensions/kernel are invalid";return false;}
            Configure(13,input,output,inputChannels,outputChannels,kernelX,kernelY,weightBase,0,0,0);
            layerMaterial.SetTexture("_ResidualTex",residual);
            VRCGraphics.Blit(input,output,layerMaterial,0);return true;
        }

        public bool DispatchBatchNormRelu(RenderTexture input,RenderTexture output,int channels,int meanBase,int varianceBase,int scaleBase,int biasBase,bool hasScale,float epsilon)
        {
            if(!Validate(input,output,channels,channels))return false;
            if(epsilon<=0f){lastError="batch norm epsilon must be positive";return false;}
            Configure(8,input,output,channels,channels,1,1,0,scaleBase,biasBase,1);
            layerMaterial.SetInt("_MeanBase",meanBase);
            layerMaterial.SetInt("_VarianceBase",varianceBase);
            layerMaterial.SetInt("_HasScale",hasScale?1:0);
            layerMaterial.SetFloat("_Epsilon",epsilon);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public bool DispatchAdd(RenderTexture input,RenderTexture residual,RenderTexture output,int channels)
        {
            if(!Validate(input,output,channels,channels)||residual==null){lastError="add inputs/outputs are invalid";return false;}
            Configure(2,input,output,channels,channels,1,1,0,0,0,0);
            layerMaterial.SetTexture("_ResidualTex",residual);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public RenderTexture CreateVector(int channels)
        {
            if(channels<=0){lastError="vector channels must be positive";return null;}
            var texture=new RenderTexture((channels+3)/4,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            texture.name="PureUdonGo.Vector."+channels;
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.useMipMap=false;
            texture.autoGenerateMips=false;
            texture.Create();
            return texture;
        }

        public RenderTexture CreatePackedReadback(int width,int height)
        {
            if(width<=0||height<=0){lastError="packed readback dimensions must be positive";return null;}
            var texture=new RenderTexture(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            texture.name="PureUdonGo.PackedReadback";
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.useMipMap=false;
            texture.autoGenerateMips=false;
            texture.Create();
            return texture;
        }

        public bool DispatchGlobalPool(RenderTexture input,RenderTexture output,int channels)
        {
            if(!ValidatePool(input,output,channels))return false;
            Configure(3,input,output,channels,channels*3,1,1,0,0,0,0);
            layerMaterial.SetInt("_VectorLength",channels*3);
            layerMaterial.SetInt("_PoolChannels",channels);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public bool DispatchValuePool(RenderTexture input,RenderTexture output,int channels)
        {
            if(!ValidatePool(input,output,channels))return false;
            Configure(4,input,output,channels,channels*3,1,1,0,0,0,0);
            layerMaterial.SetInt("_VectorLength",channels*3);
            layerMaterial.SetInt("_PoolChannels",channels);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public bool DispatchMatMul(RenderTexture input,RenderTexture output,int inputChannels,int outputChannels,int weightBase)
        {
            if(!ValidateVector(input,output,inputChannels,outputChannels))return false;
            Configure(5,input,output,inputChannels,outputChannels,1,1,weightBase,0,0,0);
            layerMaterial.SetInt("_VectorLength",inputChannels);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public bool DispatchVectorBias(RenderTexture input,RenderTexture output,int channels,int biasBase,bool relu)
        {
            if(!ValidateVector(input,output,channels,channels))return false;
            Configure(6,input,output,channels,channels,1,1,0,0,biasBase,relu?1:0);
            layerMaterial.SetInt("_VectorLength",channels);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        public bool DispatchAddVectorToActivation(RenderTexture input,RenderTexture vector,RenderTexture output,int channels)
        {
            if(!Validate(input,output,channels,channels)||vector==null||vector.height!=1||vector.width!=(channels+3)/4){lastError="vector bias dimensions are invalid";return false;}
            Configure(7,input,output,channels,channels,1,1,0,0,0,0);
            layerMaterial.SetTexture("_ResidualTex",vector);
            layerMaterial.SetInt("_VectorLength",channels);
            layerMaterial.SetInt("_ResidualWidth",vector.width);
            VRCGraphics.Blit(input,output,layerMaterial,0); return true;
        }

        /// <summary>
        /// Coalesces the policy and ownership heads into one spatial readback.
        /// The values are copied without arithmetic so the coalescing pass does
        /// not alter the neural results consumed by search.
        /// </summary>
        public bool DispatchPackSpatial(RenderTexture policy,RenderTexture ownership,RenderTexture output)
        {
            if(!Validate(policy,output,1,2)||ownership==null||
                ownership.width!=BOARD_SIZE||ownership.height!=BOARD_SIZE)
            {
                lastError="packed spatial output dimensions are invalid";return false;
            }
            Configure(9,policy,output,1,2,1,1,0,0,0,0);
            layerMaterial.SetTexture("_ResidualTex",ownership);
            VRCGraphics.Blit(policy,output,layerMaterial,0); return true;
        }

        /// <summary>
        /// Packs policy-pass/value into pixel zero and score into pixel one of
        /// an 8-channel vector texture. This reduces serial readback stages
        /// while preserving all policy, value and score components exactly.
        /// </summary>
        public bool DispatchPackVector(RenderTexture policyPass,RenderTexture value,
            RenderTexture score,RenderTexture output)
        {
            if(!ValidateVector(policyPass,output,1,8)||value==null||score==null||
                value.width!=1||value.height!=1||score.width!=1||score.height!=1)
            {
                lastError="packed vector output dimensions are invalid";return false;
            }
            Configure(10,policyPass,output,1,8,1,1,0,0,0,0);
            layerMaterial.SetTexture("_ResidualTex",value);
            layerMaterial.SetTexture("_PackTex2",score);
            VRCGraphics.Blit(policyPass,output,layerMaterial,0); return true;
        }

        /// <summary>
        /// Packs all search-consumed heads into one 19x20 readback texture.
        /// Rows 0..18 contain policy in R and optional ownership in G; row 19
        /// contains policy-pass/value at x=0 and score at x=1.
        /// </summary>
        public bool DispatchPackReadback(RenderTexture policy,RenderTexture ownership,
            RenderTexture policyPass,RenderTexture value,RenderTexture score,
            RenderTexture output,bool includeOwnership)
        {
            if(layerMaterial==null||policy==null||policyPass==null||
                value==null||score==null||output==null||policy.width!=BOARD_SIZE||
                policy.height!=BOARD_SIZE||(includeOwnership&&ownership==null)||
                (ownership!=null&&(ownership.width!=BOARD_SIZE||ownership.height!=BOARD_SIZE))||
                policyPass.width!=1||policyPass.height!=1||value.width!=1||value.height!=1||
                score.width!=1||score.height!=1||output.width!=BOARD_SIZE||
                output.height!=BOARD_SIZE+1)
            {
                lastError="packed readback dimensions are invalid";return false;
            }
            Configure(11,policy,output,1,1,1,1,0,0,0,0);
            layerMaterial.SetTexture("_ResidualTex",ownership==null?policy:ownership);
            layerMaterial.SetTexture("_PackTex2",policyPass);
            layerMaterial.SetTexture("_PackTex3",value);
            layerMaterial.SetTexture("_PackTex4",score);
            layerMaterial.SetInt("_PackOwnership",includeOwnership?1:0);
            VRCGraphics.Blit(policy,output,layerMaterial,0); return true;
        }

        private bool Validate(RenderTexture input,RenderTexture output,int inputChannels,int outputChannels)
        {
            if(layerMaterial==null){lastError="layer material is not assigned";return false;}
            if(weightTexture==null){lastError="weight texture is not assigned";return false;}
            if(input==null||output==null){lastError="input/output RenderTexture is null";return false;}
            if(input.width!=BOARD_SIZE||output.width!=BOARD_SIZE){lastError="activation width must be 19";return false;}
            if(input.height!=BOARD_SIZE*((inputChannels+3)/4)||output.height!=BOARD_SIZE*((outputChannels+3)/4)){lastError="activation height does not match channel count";return false;}
            return true;
        }

        private bool ValidateVector(RenderTexture input,RenderTexture output,int inputChannels,int outputChannels)
        {
            if(layerMaterial==null){lastError="layer material is not assigned";return false;}
            if(weightTexture==null){lastError="weight texture is not assigned";return false;}
            if(input==null||output==null||input.height!=1||output.height!=1){lastError="vector textures must have height 1";return false;}
            if(input.width!=(inputChannels+3)/4||output.width!=(outputChannels+3)/4){lastError="vector width does not match channel count";return false;}
            return true;
        }

        private bool ValidatePool(RenderTexture input,RenderTexture output,int channels)
        {
            if(layerMaterial==null){lastError="layer material is not assigned";return false;}
            if(weightTexture==null){lastError="weight texture is not assigned";return false;}
            if(input==null||output==null){lastError="pool input/output RenderTexture is null";return false;}
            if(input.width!=BOARD_SIZE||input.height!=BOARD_SIZE*((channels+3)/4)){lastError="pool input dimensions do not match channel count";return false;}
            if(output.height!=1||output.width!=(channels*3+3)/4){lastError="pool output dimensions do not match channel count";return false;}
            return true;
        }

        private void Configure(int mode,RenderTexture input,RenderTexture output,int inputChannels,int outputChannels,int kernelX,int kernelY,int weightBase,int scaleBase,int biasBase,int relu)
        {
            layerMaterial.SetInt("_Mode",mode);
            layerMaterial.SetInt("_BoardSize",BOARD_SIZE);
            layerMaterial.SetInt("_InputChannels",inputChannels);
            layerMaterial.SetInt("_OutputChannels",outputChannels);
            layerMaterial.SetInt("_KernelX",kernelX);
            layerMaterial.SetInt("_KernelY",kernelY);
            layerMaterial.SetInt("_InputWidth",input.width);
            layerMaterial.SetInt("_InputHeight",input.height);
            layerMaterial.SetInt("_OutputWidth",output.width);
            layerMaterial.SetInt("_OutputHeight",output.height);
            layerMaterial.SetInt("_WeightWidth",weightWidth);
            layerMaterial.SetInt("_WeightHeight",weightHeight);
            layerMaterial.SetInt("_WeightBase",weightBase);
            layerMaterial.SetInt("_ScaleBase",scaleBase);
            layerMaterial.SetInt("_BiasBase",biasBase);
            layerMaterial.SetInt("_MeanBase",0);
            layerMaterial.SetInt("_VarianceBase",0);
            layerMaterial.SetInt("_HasScale",0);
            layerMaterial.SetInt("_ApplyRelu",relu);
            layerMaterial.SetInt("_VectorLength",inputChannels);
            layerMaterial.SetInt("_PoolChannels",inputChannels);
            layerMaterial.SetInt("_ResidualWidth",input.width);
            layerMaterial.SetFloat("_Epsilon",0.001f);
            layerMaterial.SetTexture("_Weights",weightTexture);
        }
    }
}
