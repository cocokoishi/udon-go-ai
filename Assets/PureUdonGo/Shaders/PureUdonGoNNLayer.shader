Shader "PureUdonGo/NNLayer"
{
    Properties
    {
        _MainTex ("Input", 2D) = "black" {}
        _ResidualTex ("Residual", 2D) = "black" {}
        _PackTex2 ("Pack Input 2", 2D) = "black" {}
        _PackTex3 ("Pack Input 3", 2D) = "black" {}
        _PackTex4 ("Pack Input 4", 2D) = "black" {}
        _Weights ("Weight Atlas", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _ResidualTex;
            sampler2D _PackTex2;
            sampler2D _PackTex3;
            sampler2D _PackTex4;
            sampler2D _Weights;

            int _Mode;
            int _BoardSize;
            int _InputChannels;
            int _OutputChannels;
            int _KernelX;
            int _KernelY;
            int _InputWidth;
            int _InputHeight;
            int _OutputWidth;
            int _OutputHeight;
            int _WeightWidth;
            int _WeightHeight;
            int _WeightBase;
            int _ScaleBase;
            int _BiasBase;
            int _MeanBase;
            int _VarianceBase;
            int _HasScale;
            int _ApplyRelu;
            int _VectorLength;
            int _PoolChannels;
            int _ResidualWidth;
            int _PackOwnership;
            float _Epsilon;

            float LoadWeight(int floatIndex)
            {
                if (floatIndex < 0 || floatIndex >= _WeightWidth * _WeightHeight * 4)
                    return 0.0;
                int texel = floatIndex / 4;
                int lane = floatIndex - texel * 4;
                int2 xy = int2(texel % _WeightWidth, texel / _WeightWidth);
                float4 value = tex2Dlod(_Weights, float4((float2(xy) + 0.5) / float2(_WeightWidth, _WeightHeight), 0, 0));
                if (lane == 0) return value.x;
                if (lane == 1) return value.y;
                if (lane == 2) return value.z;
                return value.w;
            }

            float4 LoadWeight4(int floatIndex)
            {
                if (floatIndex < 0 || floatIndex + 3 >= _WeightWidth * _WeightHeight * 4)
                    return 0.0;
                int texel = floatIndex / 4;
                int2 xy = int2(texel % _WeightWidth, texel / _WeightWidth);
                return tex2Dlod(_Weights, float4((float2(xy) + 0.5) / float2(_WeightWidth, _WeightHeight), 0, 0));
            }

            float LoadActivation(sampler2D textureSampler, int x, int y, int channel, int width, int height)
            {
                if (x < 0 || x >= width || y < 0 || y >= _BoardSize || channel < 0 || channel >= _InputChannels)
                    return 0.0;
                int groups = (channel / 4);
                int lane = channel - groups * 4;
                int textureY = y + groups * _BoardSize;
                if (textureY < 0 || textureY >= height)
                    return 0.0;
                float2 uv = (float2(x, textureY) + 0.5) / float2(width, height);
                float4 value = tex2Dlod(textureSampler, float4(uv, 0, 0));
                return value[lane];
            }

            float4 LoadActivation4(sampler2D textureSampler, int x, int y, int group, int width, int height, int channels)
            {
                int textureY = y + group * _BoardSize;
                if (x < 0 || x >= width || y < 0 || y >= _BoardSize || textureY < 0 || textureY >= height)
                    return 0.0;
                float2 uv = (float2(x, textureY) + 0.5) / float2(width, height);
                return tex2Dlod(textureSampler, float4(uv, 0, 0));
            }

            float ConvolutionChannel(int x, int y, int outputChannel)
            {
                int radiusX = _KernelX / 2;
                int radiusY = _KernelY / 2;
                float sum = 0.0;
                for (int ky = 0; ky < 5; ky++)
                {
                    if (ky >= _KernelY) break;
                    for (int kx = 0; kx < 5; kx++)
                    {
                        if (kx >= _KernelX) break;
                        int inputX = x + kx - radiusX;
                        int inputY = y + ky - radiusY;
                        for (int inputChannel = 0; inputChannel < 128; inputChannel++)
                        {
                            if (inputChannel >= _InputChannels) break;
                            int weightOffset = _WeightBase + ((ky * _KernelX + kx) * _InputChannels + inputChannel) * _OutputChannels + outputChannel;
                            sum += LoadActivation(_MainTex, inputX, inputY, inputChannel, _InputWidth, _InputHeight) * LoadWeight(weightOffset);
                        }
                    }
                }
                return sum;
            }

            float4 ConvolutionGroup(int x, int y, int outputBase)
            {
                int radiusX = _KernelX / 2;
                int radiusY = _KernelY / 2;
                float4 sum = 0.0;
                for (int ky = 0; ky < 5; ky++)
                {
                    if (ky >= _KernelY) break;
                    for (int kx = 0; kx < 5; kx++)
                    {
                        if (kx >= _KernelX) break;
                        int inputX = x + kx - radiusX;
                        int inputY = y + ky - radiusY;
                        for (int inputGroup = 0; inputGroup < 32; inputGroup++)
                        {
                            int inputBase = inputGroup * 4;
                            if (inputBase >= _InputChannels) break;
                            float4 input = LoadActivation4(_MainTex, inputX, inputY, inputGroup, _InputWidth, _InputHeight, _InputChannels);
                            for (int lane = 0; lane < 4; lane++)
                            {
                                int inputChannel = inputBase + lane;
                                if (inputChannel >= _InputChannels) break;
                                int weightOffset = _WeightBase + ((ky * _KernelX + kx) * _InputChannels + inputChannel) * _OutputChannels + outputBase;
                                sum += input[lane] * LoadWeight4(weightOffset);
                            }
                        }
                    }
                }
                return sum;
            }

            float4 FragmentConvolution(int x, int y, int group)
            {
                float4 result = 0.0;
                int outputBase = group * 4;
                if (_OutputChannels >= 4 && (_OutputChannels % 4) == 0)
                    return ConvolutionGroup(x, y, outputBase);
                if (outputBase < _OutputChannels) result.x = ConvolutionChannel(x, y, outputBase);
                if (outputBase + 1 < _OutputChannels) result.y = ConvolutionChannel(x, y, outputBase + 1);
                if (outputBase + 2 < _OutputChannels) result.z = ConvolutionChannel(x, y, outputBase + 2);
                if (outputBase + 3 < _OutputChannels) result.w = ConvolutionChannel(x, y, outputBase + 3);
                return result;
            }

            float4 FragmentAffine(int x, int y, int group)
            {
                float4 input = LoadActivation4(_MainTex, x, y, group, _InputWidth, _InputHeight, _InputChannels);
                float4 result = input;
                int channel = group * 4;
                if ((_ScaleBase % 4) != 0 || (_BiasBase % 4) != 0)
                {
                    if (channel < _OutputChannels) { result.x = input.x * LoadWeight(_ScaleBase + channel) + LoadWeight(_BiasBase + channel); if (_ApplyRelu != 0) result.x = max(result.x, 0.0); } else result.x = 0.0;
                    if (channel + 1 < _OutputChannels) { result.y = input.y * LoadWeight(_ScaleBase + channel + 1) + LoadWeight(_BiasBase + channel + 1); if (_ApplyRelu != 0) result.y = max(result.y, 0.0); } else result.y = 0.0;
                    if (channel + 2 < _OutputChannels) { result.z = input.z * LoadWeight(_ScaleBase + channel + 2) + LoadWeight(_BiasBase + channel + 2); if (_ApplyRelu != 0) result.z = max(result.z, 0.0); } else result.z = 0.0;
                    if (channel + 3 < _OutputChannels) { result.w = input.w * LoadWeight(_ScaleBase + channel + 3) + LoadWeight(_BiasBase + channel + 3); if (_ApplyRelu != 0) result.w = max(result.w, 0.0); } else result.w = 0.0;
                    return result;
                }
                float4 scale = LoadWeight4(_ScaleBase + channel);
                float4 bias = LoadWeight4(_BiasBase + channel);
                result = input * scale + bias;
                if (_ApplyRelu != 0) result = max(result, 0.0);
                if (channel >= _OutputChannels) result.x = 0.0;
                if (channel + 1 >= _OutputChannels) result.y = 0.0;
                if (channel + 2 >= _OutputChannels) result.z = 0.0;
                if (channel + 3 >= _OutputChannels) result.w = 0.0;
                return result;
            }

            float4 FragmentBatchNorm(int x, int y, int group)
            {
                float4 input = LoadActivation4(_MainTex, x, y, group, _InputWidth, _InputHeight, _InputChannels);
                float4 result = input;
                int channel = group * 4;
                if ((_MeanBase % 4) != 0 || (_VarianceBase % 4) != 0 ||
                    (_HasScale != 0 && (_ScaleBase % 4) != 0) || (_BiasBase % 4) != 0)
                {
                    if (channel < _OutputChannels) { result.x = (input.x - LoadWeight(_MeanBase + channel)) / sqrt(LoadWeight(_VarianceBase + channel) + _Epsilon); if (_HasScale != 0) result.x *= LoadWeight(_ScaleBase + channel); result.x += LoadWeight(_BiasBase + channel); if (_ApplyRelu != 0) result.x = max(result.x, 0.0); } else result.x = 0.0;
                    if (channel + 1 < _OutputChannels) { result.y = (input.y - LoadWeight(_MeanBase + channel + 1)) / sqrt(LoadWeight(_VarianceBase + channel + 1) + _Epsilon); if (_HasScale != 0) result.y *= LoadWeight(_ScaleBase + channel + 1); result.y += LoadWeight(_BiasBase + channel + 1); if (_ApplyRelu != 0) result.y = max(result.y, 0.0); } else result.y = 0.0;
                    if (channel + 2 < _OutputChannels) { result.z = (input.z - LoadWeight(_MeanBase + channel + 2)) / sqrt(LoadWeight(_VarianceBase + channel + 2) + _Epsilon); if (_HasScale != 0) result.z *= LoadWeight(_ScaleBase + channel + 2); result.z += LoadWeight(_BiasBase + channel + 2); if (_ApplyRelu != 0) result.z = max(result.z, 0.0); } else result.z = 0.0;
                    if (channel + 3 < _OutputChannels) { result.w = (input.w - LoadWeight(_MeanBase + channel + 3)) / sqrt(LoadWeight(_VarianceBase + channel + 3) + _Epsilon); if (_HasScale != 0) result.w *= LoadWeight(_ScaleBase + channel + 3); result.w += LoadWeight(_BiasBase + channel + 3); if (_ApplyRelu != 0) result.w = max(result.w, 0.0); } else result.w = 0.0;
                    return result;
                }
                float4 mean = LoadWeight4(_MeanBase + channel);
                float4 variance = LoadWeight4(_VarianceBase + channel);
                result = (input - mean) / sqrt(variance + _Epsilon);
                if (_HasScale != 0) result *= LoadWeight4(_ScaleBase + channel);
                result += LoadWeight4(_BiasBase + channel);
                if (_ApplyRelu != 0) result = max(result, 0.0);
                if (channel >= _OutputChannels) result.x = 0.0;
                if (channel + 1 >= _OutputChannels) result.y = 0.0;
                if (channel + 2 >= _OutputChannels) result.z = 0.0;
                if (channel + 3 >= _OutputChannels) result.w = 0.0;
                return result;
            }

            float4 NormalizeConvolutionResult(float4 input, int group)
            {
                float4 result = input;
                int channel = group * 4;
                if ((_MeanBase % 4) != 0 || (_VarianceBase % 4) != 0 ||
                    (_HasScale != 0 && (_ScaleBase % 4) != 0) || (_BiasBase % 4) != 0)
                {
                    if (channel < _OutputChannels) { result.x = (input.x - LoadWeight(_MeanBase + channel)) / sqrt(LoadWeight(_VarianceBase + channel) + _Epsilon); if (_HasScale != 0) result.x *= LoadWeight(_ScaleBase + channel); result.x += LoadWeight(_BiasBase + channel); if (_ApplyRelu != 0) result.x = max(result.x, 0.0); } else result.x = 0.0;
                    if (channel + 1 < _OutputChannels) { result.y = (input.y - LoadWeight(_MeanBase + channel + 1)) / sqrt(LoadWeight(_VarianceBase + channel + 1) + _Epsilon); if (_HasScale != 0) result.y *= LoadWeight(_ScaleBase + channel + 1); result.y += LoadWeight(_BiasBase + channel + 1); if (_ApplyRelu != 0) result.y = max(result.y, 0.0); } else result.y = 0.0;
                    if (channel + 2 < _OutputChannels) { result.z = (input.z - LoadWeight(_MeanBase + channel + 2)) / sqrt(LoadWeight(_VarianceBase + channel + 2) + _Epsilon); if (_HasScale != 0) result.z *= LoadWeight(_ScaleBase + channel + 2); result.z += LoadWeight(_BiasBase + channel + 2); if (_ApplyRelu != 0) result.z = max(result.z, 0.0); } else result.z = 0.0;
                    if (channel + 3 < _OutputChannels) { result.w = (input.w - LoadWeight(_MeanBase + channel + 3)) / sqrt(LoadWeight(_VarianceBase + channel + 3) + _Epsilon); if (_HasScale != 0) result.w *= LoadWeight(_ScaleBase + channel + 3); result.w += LoadWeight(_BiasBase + channel + 3); if (_ApplyRelu != 0) result.w = max(result.w, 0.0); } else result.w = 0.0;
                    return result;
                }
                float4 mean = LoadWeight4(_MeanBase + channel);
                float4 variance = LoadWeight4(_VarianceBase + channel);
                result = (input - mean) / sqrt(variance + _Epsilon);
                if (_HasScale != 0) result *= LoadWeight4(_ScaleBase + channel);
                result += LoadWeight4(_BiasBase + channel);
                if (_ApplyRelu != 0) result = max(result, 0.0);
                if (channel >= _OutputChannels) result.x = 0.0;
                if (channel + 1 >= _OutputChannels) result.y = 0.0;
                if (channel + 2 >= _OutputChannels) result.z = 0.0;
                if (channel + 3 >= _OutputChannels) result.w = 0.0;
                return result;
            }


            float4 FragmentAdd(int x, int y, int group)
            {
                return LoadActivation4(_MainTex, x, y, group, _InputWidth, _InputHeight, _InputChannels)
                    + LoadActivation4(_ResidualTex, x, y, group, _InputWidth, _InputHeight, _InputChannels);
            }

            float LoadVector(sampler2D textureSampler, int index, int width)
            {
                if (index < 0 || index >= _VectorLength)
                    return 0.0;
                int group = index / 4;
                int lane = index - group * 4;
                float2 uv = (float2(group, 0) + 0.5) / float2(width, 1);
                float4 value = tex2Dlod(textureSampler, float4(uv, 0, 0));
                if (lane == 0) return value.x;
                if (lane == 1) return value.y;
                if (lane == 2) return value.z;
                return value.w;
            }

            float4 LoadVector4(sampler2D textureSampler, int group, int width)
            {
                if (group < 0 || group >= width)
                    return 0.0;
                float2 uv = (float2(group, 0) + 0.5) / float2(width, 1);
                return tex2Dlod(textureSampler, float4(uv, 0, 0));
            }

            float GPoolChannel(int channel, int valueMode)
            {
                float sum = 0.0;
                float maximum = -3.402823e+38;
                int inputChannel = channel % _PoolChannels;
                for (int y = 0; y < 19; y++)
                {
                    for (int x = 0; x < 19; x++)
                    {
                        float value = LoadActivation(_MainTex, x, y, inputChannel, _InputWidth, _InputHeight);
                        sum += value;
                        maximum = max(maximum, value);
                    }
                }
                float mean = sum / 361.0;
                float scale = 0.5;
                int segment = channel / _PoolChannels;
                if (valueMode == 0)
                {
                    if (segment == 0) return mean;
                    if (segment == 1) return mean * scale;
                    return maximum;
                }
                if (segment == 0) return mean;
                if (segment == 1) return mean * scale;
                return mean * 0.15;
            }

            float4 FragmentGPool(int group, int valueMode)
            {
                int outputBase = group * 4;
                float4 result = 0.0;
                if (outputBase < _OutputChannels) result.x = GPoolChannel(outputBase % (_PoolChannels * 3), valueMode);
                if (outputBase + 1 < _OutputChannels) result.y = GPoolChannel((outputBase + 1) % (_PoolChannels * 3), valueMode);
                if (outputBase + 2 < _OutputChannels) result.z = GPoolChannel((outputBase + 2) % (_PoolChannels * 3), valueMode);
                if (outputBase + 3 < _OutputChannels) result.w = GPoolChannel((outputBase + 3) % (_PoolChannels * 3), valueMode);
                return result;
            }

            float MatMulChannel(int outputChannel)
            {
                float sum = 0.0;
                for (int inputChannel = 0; inputChannel < 128; inputChannel++)
                {
                    if (inputChannel >= _InputChannels) break;
                    sum += LoadVector(_MainTex, inputChannel, _InputWidth) * LoadWeight(_WeightBase + inputChannel * _OutputChannels + outputChannel);
                }
                return sum;
            }

            float4 MatMulGroup(int outputBase)
            {
                float4 sum = 0.0;
                for (int inputGroup = 0; inputGroup < 32; inputGroup++)
                {
                    int inputBase = inputGroup * 4;
                    if (inputBase >= _InputChannels) break;
                    float4 input = LoadVector4(_MainTex, inputGroup, _InputWidth);
                    for (int lane = 0; lane < 4; lane++)
                    {
                        int inputChannel = inputBase + lane;
                        if (inputChannel >= _InputChannels) break;
                        int weightOffset = _WeightBase + inputChannel * _OutputChannels + outputBase;
                        sum += input[lane] * LoadWeight4(weightOffset);
                    }
                }
                return sum;
            }

            float4 FragmentMatMul(int group)
            {
                int outputBase = group * 4;
                if (_OutputChannels >= 4 && (_OutputChannels % 4) == 0 && (_WeightBase % 4) == 0)
                    return MatMulGroup(outputBase);
                float4 result = 0.0;
                if (outputBase < _OutputChannels) result.x = MatMulChannel(outputBase);
                if (outputBase + 1 < _OutputChannels) result.y = MatMulChannel(outputBase + 1);
                if (outputBase + 2 < _OutputChannels) result.z = MatMulChannel(outputBase + 2);
                if (outputBase + 3 < _OutputChannels) result.w = MatMulChannel(outputBase + 3);
                return result;
            }

            float4 FragmentVectorBias(int group)
            {
                float4 input = LoadActivation4(_MainTex, group, 0, 0, _InputWidth, 1, _InputChannels);
                float4 result = input;
                int channel = group * 4;
                if (channel < _OutputChannels) { result.x = input.x + LoadWeight(_BiasBase + channel); if (_ApplyRelu != 0) result.x = max(result.x, 0.0); } else result.x = 0.0;
                if (channel + 1 < _OutputChannels) { result.y = input.y + LoadWeight(_BiasBase + channel + 1); if (_ApplyRelu != 0) result.y = max(result.y, 0.0); } else result.y = 0.0;
                if (channel + 2 < _OutputChannels) { result.z = input.z + LoadWeight(_BiasBase + channel + 2); if (_ApplyRelu != 0) result.z = max(result.z, 0.0); } else result.z = 0.0;
                if (channel + 3 < _OutputChannels) { result.w = input.w + LoadWeight(_BiasBase + channel + 3); if (_ApplyRelu != 0) result.w = max(result.w, 0.0); } else result.w = 0.0;
                return result;
            }

            float4 FragmentAddVectorToActivation(int x, int y, int group)
            {
                float4 input = LoadActivation4(_MainTex, x, y, group, _InputWidth, _InputHeight, _InputChannels);
                int channel = group * 4;
                float4 result = input;
                if (channel < _OutputChannels) result.x = input.x + LoadVector(_ResidualTex, channel, _ResidualWidth); else result.x = 0.0;
                if (channel + 1 < _OutputChannels) result.y = input.y + LoadVector(_ResidualTex, channel + 1, _ResidualWidth); else result.y = 0.0;
                if (channel + 2 < _OutputChannels) result.z = input.z + LoadVector(_ResidualTex, channel + 2, _ResidualWidth); else result.z = 0.0;
                if (channel + 3 < _OutputChannels) result.w = input.w + LoadVector(_ResidualTex, channel + 3, _ResidualWidth); else result.w = 0.0;
                return result;
            }

            float4 FragmentPackSpatial(int x, int y)
            {
                float2 uv = (float2(x, y) + 0.5) / float2(_InputWidth, _InputHeight);
                float policy = tex2Dlod(_MainTex, float4(uv, 0, 0)).r;
                float ownership = tex2Dlod(_ResidualTex, float4(uv, 0, 0)).r;
                return float4(policy, ownership, 0.0, 0.0);
            }

            float4 FragmentPackVector(int x)
            {
                if (x == 0)
                {
                    float4 policy = tex2Dlod(_MainTex, float4(0.5 / _InputWidth, 0.5, 0, 0));
                    float4 value = tex2Dlod(_ResidualTex, float4(0.5, 0.5, 0, 0));
                    return float4(policy.r, value.r, value.g, value.b);
                }
                return tex2Dlod(_PackTex2, float4(0.5, 0.5, 0, 0));
            }

            float4 FragmentPackReadback(int x, int y)
            {
                if (y < _BoardSize)
                {
                    float2 uv = (float2(x, y) + 0.5) / float2(_InputWidth, _InputHeight);
                    float policy = tex2Dlod(_MainTex, float4(uv, 0, 0)).r;
                    float ownership = tex2Dlod(_ResidualTex, float4(uv, 0, 0)).r;
                    return float4(policy, _PackOwnership != 0 ? ownership : 0.0, 0.0, 0.0);
                }
                if (x == 0)
                {
                    float4 policyPass = tex2Dlod(_PackTex2, float4(0.5, 0.5, 0, 0));
                    float4 value = tex2Dlod(_PackTex3, float4(0.5, 0.5, 0, 0));
                    return float4(policyPass.r, value.r, value.g, value.b);
                }
                if (x == 1)
                    return tex2Dlod(_PackTex4, float4(0.5, 0.5, 0, 0));
                return 0.0;
            }

            float4 frag(v2f_img input) : SV_Target
            {
                int2 pixel = int2(input.pos.xy);
                if (_Mode == 9)
                    return FragmentPackSpatial(pixel.x, pixel.y);
                if (_Mode == 10)
                    return FragmentPackVector(pixel.x);
                if (_Mode == 11)
                    return FragmentPackReadback(pixel.x, pixel.y);
                if (_Mode == 3 || _Mode == 4)
                    return FragmentGPool(pixel.x, _Mode == 4 ? 1 : 0);
                if (_Mode == 5)
                    return FragmentMatMul(pixel.x);
                if (_Mode == 6)
                    return FragmentVectorBias(pixel.x);
                int x = pixel.x;
                int outputY = pixel.y;
                int y = outputY % _BoardSize;
                int group = outputY / _BoardSize;
                if (x < 0 || x >= _OutputWidth || outputY < 0 || outputY >= _OutputHeight)
                    return 0.0;
                if (_Mode == 0)
                    return FragmentConvolution(x, y, group);
                if (_Mode == 12)
                {
                    // Explicit float intermediate retains the original sum
                    // before the normalization. Numeric gate still required:
                    // shader compilers may contract operations across passes.
                    precise float4 convolution = FragmentConvolution(x, y, group);
                    return NormalizeConvolutionResult(convolution, group);
                }
                if (_Mode == 13)
                {
                    precise float4 convolution = FragmentConvolution(x, y, group);
                    float4 residual = LoadActivation4(_ResidualTex, x, y, group,
                        _OutputWidth, _OutputHeight, _OutputChannels);
                    return residual + convolution;
                }
                if (_Mode == 1)
                    return FragmentAffine(x, y, group);
                if (_Mode == 8)
                    return FragmentBatchNorm(x, y, group);
                if (_Mode == 2)
                    return FragmentAdd(x, y, group);
                if (_Mode == 7)
                    return FragmentAddVectorToActivation(x, y, group);
                return 0.0;
            }
            ENDHLSL
        }
    }
}
