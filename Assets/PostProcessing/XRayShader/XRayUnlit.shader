Shader "Custom/XRayUnlit"
{
    // ════════════════════════════════════════════════════════════════════════
    // XRay Unlit Shader
    //
    // Rendert ein Objekt:
    //   - Unlit (keine Beleuchtung, reine Farbe)
    //   - Transparent (Alpha-Blending, unterstützt Fade)
    //   - Durch Wände sichtbar (ZTest Always - ignoriert Tiefenpuffer)
    //   - Kein ZWrite (schreibt nicht in den Tiefenpuffer)
    //
    // Use Case: Reveal-Meshes für "Ninja-Sinne"-Scan-Effekt.
    //
    // Properties:
    //   _BaseColor  - Basisfarbe inkl. Alpha (Alpha steuert Fade)
    //   _BaseMap    - Optional: Textur (wenn nicht gesetzt: weiß)
    // ════════════════════════════════════════════════════════════════════════

    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayUnlit"
            Tags { "LightMode" = "UniversalForward" }

            // Render State: durch Wände, transparent, keine Tiefe schreiben
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                return texColor * _BaseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
