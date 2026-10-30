// GateRush VR: the drone camera feed on the goggles screen, with analog-video artefacts.
// _Noise (0..1): snow, desaturation, scanlines. _Breakup (0..1): line jitter and static bands (weak signal).
// Unlit, opaque, single-pass-instanced stereo; cheap enough for the Quest eye buffer (one texture sample + hashes).
Shader "GateRush/AnalogFeed"
{
    Properties
    {
        _BaseMap ("Feed", 2D) = "black" {}
        _Noise ("Noise", Range(0, 1)) = 0
        _Breakup ("Breakup", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Noise;
                float _Breakup;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                float2 uv = i.uv;

                // Weak signal: whole lines slip sideways.
                float scan = floor(uv.y * 240.0);
                uv.x += (Hash(float2(scan, floor(t * 60.0))) - 0.5) * 0.03 * _Breakup;

                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

                // Snow + colour loss + scanlines.
                float snow = Hash(uv * float2(640.0, 360.0) + frac(t * 37.0) * 100.0);
                c = lerp(c, snow.xxx, _Noise * 0.55);
                float luma = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(c, luma.xxx, _Noise * 0.7);
                c *= 1.0 - 0.1 * _Noise * fmod(scan, 2.0);

                // Static bands roll through on a breaking-up signal.
                float band = step(1.0 - _Breakup * 0.35, Hash(float2(floor(uv.y * 18.0), floor(t * 14.0))));
                c = lerp(c, snow.xxx, band);

                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
