Shader "Hidden/PixelSorting"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // Compile-time maximum. The actual count is clamped to _PassCount at runtime.
        #define MAX_STEPS 64

        float2 _SortDirection;
        float  _ThresholdMin;
        float  _ThresholdMax;
        float  _Intensity;
        float  _SortCriterion;
        float  _StepSize;
        float  _PassCount;
        float  _DebugMode;

        float GetLuminance(float3 c)
        {
            return dot(c, float3(0.2126, 0.7152, 0.0722));
        }

        float3 RGBtoHSV(float3 c)
        {
            float cMax = max(c.r, max(c.g, c.b));
            float cMin = min(c.r, min(c.g, c.b));
            float delta = cMax - cMin;

            float h = 0.0;
            float s = (cMax > 0.0001) ? (delta / cMax) : 0.0;
            float v = cMax;

            if (delta > 0.0001)
            {
                if (cMax == c.r)
                    h = (c.g - c.b) / delta + (c.g < c.b ? 6.0 : 0.0);
                else if (cMax == c.g)
                    h = (c.b - c.r) / delta + 2.0;
                else
                    h = (c.r - c.g) / delta + 4.0;
                h /= 6.0;
            }

            return float3(h, s, v);
        }

        float GetSortKey(float3 color)
        {
            if (_SortCriterion < 0.5)
                return GetLuminance(color);

            float3 hsv = RGBtoHSV(color);
            if (_SortCriterion < 1.5)
                return hsv.x;
            return hsv.y;
        }

        bool IsInThreshold(float key)
        {
            return key >= _ThresholdMin && key <= _ThresholdMax;
        }

        ENDHLSL

        Pass
        {
            Name "PixelSortPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSort

            float4 FragSort(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                // ── Debug: Red tint ───────────────────────────
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return float4(original.r + 0.3, original.g * 0.5, original.b * 0.5, 1.0);

                // ── Debug: Threshold mask ─────────────────────
                if (_DebugMode > 1.5)
                {
                    float key = GetSortKey(original.rgb);
                    if (IsInThreshold(key))
                        return float4(0, 1, 0, 1);
                    else
                        return float4(original.rgb * 0.3, 1);
                }

                // ── Pixel sorting ─────────────────────────────
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float2 stepUV = _SortDirection * texelSize * _StepSize;

                float myKey = GetSortKey(original.rgb);

                if (!IsInThreshold(myKey))
                    return original;

                int maxSteps = min((int)_PassCount, MAX_STEPS);
                int displacement = 0;

                // Look behind: neighbors with higher key push us forward
                [loop]
                for (int i = 1; i <= MAX_STEPS; i++)
                {
                    if (i > maxSteps) break;

                    float2 sUV = uv - stepUV * (float)i;
                    sUV = clamp(sUV, texelSize * 0.5, 1.0 - texelSize * 0.5);

                    float3 nc = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, sUV, 0).rgb;
                    float nk = GetSortKey(nc);

                    if (!IsInThreshold(nk)) break;
                    if (nk > myKey)
                        displacement--;
                    else
                        break;
                }

                // Look ahead: neighbors with lower key mean we move forward
                [loop]
                for (int j = 1; j <= MAX_STEPS; j++)
                {
                    if (j > maxSteps) break;

                    float2 sUV = uv + stepUV * (float)j;
                    sUV = clamp(sUV, texelSize * 0.5, 1.0 - texelSize * 0.5);

                    float3 nc = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, sUV, 0).rgb;
                    float nk = GetSortKey(nc);

                    if (!IsInThreshold(nk)) break;
                    if (nk < myKey)
                        displacement++;
                    else
                        break;
                }

                float2 displacedUV = uv + stepUV * (float)displacement;
                displacedUV = clamp(displacedUV, texelSize * 0.5, 1.0 - texelSize * 0.5);
                float4 sorted = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, displacedUV);

                return lerp(original, sorted, _Intensity);
            }

            ENDHLSL
        }
    }
}
