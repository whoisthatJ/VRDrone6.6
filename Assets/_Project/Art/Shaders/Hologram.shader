// P3 hologram: additive, unlit, two-sided light for the HoloRing gates, the Holo Halo, its projector beams and the 3D
// guidance. Fresnel rim glow, moving scanlines, a little flicker, and a glitch (sliced vertex offsets + flashing)
// driven by _Glitch. _BaseColor / _Intensity are the same properties NextGateHighlight sets on gate LEDs, so a ring
// takes its next / later / passed colour like any gate.
Shader "Ushqyr/Hologram"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.25, 0.9, 1, 1)
        _Intensity ("Intensity", Float) = 2.5
        _Fresnel ("Fresnel power", Float) = 2
        _ScanDensity ("Scanlines per metre", Float) = 1.5
        _Flicker ("Flicker", Range(0, 1)) = 0.12
        _Glitch ("Glitch", Range(0, 1)) = 0
        _Alpha ("Base glow", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                half _Fresnel;
                half _ScanDensity;
                half _Flicker;
                half _Glitch;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 p = v.positionOS.xyz;
                // Glitch: horizontal slices jump sideways for a moment.
                float slice = floor(p.y * 6.0 + _Time.y * 30.0);
                p.x += _Glitch * (Hash(slice) - 0.5) * 0.6;
                float3 ws = TransformObjectToWorld(p);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), view))), _Fresnel);
                half scan = 0.7 + 0.3 * sin((i.positionWS.y + i.positionWS.x * 0.3) * _ScanDensity * 6.2832 - _Time.y * 5.0);
                half flicker = 1.0 - _Flicker * Hash(floor(_Time.y * 24.0));
                half glitch = 1.0 + _Glitch * 2.0 * Hash(floor(_Time.y * 40.0));
                half a = (_Alpha + fres * 1.5) * scan * flicker * glitch;
                half3 c = _BaseColor.rgb * _Intensity * a;
                // Additive: fade towards black (no light) in the fog rather than towards the fog colour.
                c *= ComputeFogIntensity(i.fog);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
