Shader "Hidden/URP/RadialPixelSort"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float2 _Center;
    float _Spread;
    int _SortCriteria; // 0 = Luminance, 1 = Hue, 2 = Saturation
    float _Threshold;

    #define SORT_SAMPLES 12

    // Helper to get Hue and Saturation
    float3 RGBToHSV(float3 c)
    {
        float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
        float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
        float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
        float d = q.x - min(q.w, q.y);
        float e = 1.0e-10;
        return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
    }

    float GetSortValue(float3 color)
    {
        if (_SortCriteria == 0) 
        {
            // Luminance
            return dot(color, float3(0.299, 0.587, 0.114));
        }
        else if (_SortCriteria == 1)
        {
            // Hue
            return RGBToHSV(color).x;
        }
        else 
        {
            // Saturation
            return RGBToHSV(color).y;
        }
    }

    half4 Frag(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 dir = uv - _Center;
        float dist = length(dir);
        
        // Normalize direction and apply spread parameter
        dir = normalize(dir) * _Spread;
        float2 stepUV = dir / SORT_SAMPLES;

        float4 colors[SORT_SAMPLES];
        float values[SORT_SAMPLES];

        // Gather samples along the radial ray
        for (int i = 0; i < SORT_SAMPLES; i++)
        {
            float2 sampleUV = uv + (stepUV * i);
            colors[i] = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV);
            values[i] = GetSortValue(colors[i].rgb);
        }

        // Apply Threshold mask: Only sort if the base pixel passes the threshold
        if (values[0] < _Threshold) return colors[0];

        // Local Bubble Sort (Efficient enough for small SORT_SAMPLES on modern GPUs)
        [unroll(SORT_SAMPLES)]
        for (int j = 0; j < SORT_SAMPLES - 1; j++)
        {
            [unroll(SORT_SAMPLES)]
            for (int k = 0; k < SORT_SAMPLES - 1 - j; k++)
            {
                if (values[k] > values[k + 1])
                {
                    // Swap values
                    float tempVal = values[k];
                    values[k] = values[k + 1];
                    values[k + 1] = tempVal;

                    // Swap colors
                    float4 tempCol = colors[k];
                    colors[k] = colors[k + 1];
                    colors[k + 1] = tempCol;
                }
            }
        }

        // Return the color at the start of our newly sorted window
        // (Changing the index here changes the 'melt' behavior)
        return colors[0]; 
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZWrite Off Cull Off

        Pass
        {
            Name "Radial Pixel Sort"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}