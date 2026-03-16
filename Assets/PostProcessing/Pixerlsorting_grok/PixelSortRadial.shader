Shader "Hidden/URP/PixelSortRadial"
{
    Properties
    {
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 2)) = 1.0
        _Threshold ("Threshold", Range(0, 1)) = 0.4
        _Displacement ("Max Displacement", Range(0, 0.5)) = 0.25
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
        [IntRange] _SortMode ("Sort Mode (0=Lum, 1=Hue, 2=Sat)", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            Name "PixelSortRadial"
            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Intensity;
            float _Threshold;
            float _Displacement;
            float2 _Center;
            int _SortMode;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float3 RGBToHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float GetKey(float3 rgb)
            {
                if (_SortMode == 0) return dot(rgb, float3(0.299, 0.587, 0.114)); // Luminance
                if (_SortMode == 1) return RGBToHSV(rgb).x;                        // Hue (circular = nice radial bands)
                return RGBToHSV(rgb).y;                                            // Saturation
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 toCenter = uv - _Center;
                float dist = length(toCenter);
                float2 dir = normalize(toCenter);

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float key = GetKey(color.rgb);

                if (key < _Threshold)
                    return color;

                // Push high-key pixels outward along the ray
                float push = (key - _Threshold) / (1.0 - _Threshold) * _Displacement * _Intensity;
                float2 sampleUV = uv + dir * push;   // sample from further out → high values appear pushed outward

                sampleUV = clamp(sampleUV, 0.0, 1.0);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV);
            }
            ENDHLSL
        }
    }
}