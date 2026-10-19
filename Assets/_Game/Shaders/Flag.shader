// Flag cloth: a grid along local +X from the pole (UV.x 0 at the pole, 1 at the fly end) waving in the breeze. The
// travelling wave grows toward the free end; both faces are lit (back faces flip the normal). A darker hem runs along
// the pole and a white band crosses the cloth.
Shader "Puttshack/Flag"
{
    Properties
    {
        _BaseColor ("Cloth", Color) = (0.86, 0.14, 0.12, 1)
        _BandColor ("Band", Color) = (0.97, 0.96, 0.92, 1)
        _Amplitude ("Wave Amplitude (m)", Float) = 0.055
        _Frequency ("Wave Frequency", Float) = 7.0
        _Speed ("Wave Speed", Float) = 6.5
        _Translucency ("Translucency", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _BandColor;
            float _Amplitude;
            float _Frequency;
            float _Speed;
            half _Translucency;
        CBUFFER_END

        // Displaced object-space position and normal of the cloth at uv.
        void Wave(float3 positionOS, float2 uv, out float3 pos, out float3 nrm)
        {
            float phase = uv.x * _Frequency - _Time.y * _Speed + uv.y * 0.9;
            float gust = 0.75 + 0.25 * sin(_Time.y * 0.7);
            float amp = _Amplitude * gust * (0.15 + 0.85 * uv.x);
            float z = sin(phase) * amp + sin(phase * 2.3 + 1.7) * amp * 0.25;
            float dz = (cos(phase) * amp * _Frequency + cos(phase * 2.3 + 1.7) * amp * 0.25 * _Frequency * 2.3) / 0.55;
            pos = positionOS + float3(-abs(z) * 0.25, -uv.x * uv.x * 0.02, z);
            nrm = normalize(float3(dz, 0, -1));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 pos, nrm;
                Wave(input.positionOS.xyz, input.uv, pos, nrm);
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 n = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1, -1);
                float3 p = input.positionWS;
                half3 V = GetWorldSpaceNormalizeViewDir(p);
                half band = smoothstep(0.42, 0.44, input.uv.y) * (1 - smoothstep(0.56, 0.58, input.uv.y));
                half3 albedo = lerp(_BaseColor.rgb, _BandColor.rgb, band);
                albedo *= lerp(0.7, 1, smoothstep(0.0, 0.05, input.uv.x));

                Light light = GetMainLight(TransformWorldToShadowCoord(p));
                half shadow = light.shadowAttenuation;
                half ndl = saturate(dot(n, light.direction));
                half back = saturate(dot(-n, light.direction)) * _Translucency;
                half3 color = albedo * (SampleSH(n) + light.color * shadow * (ndl + back));
                half3 H = normalize(light.direction + V);
                color += light.color * shadow * pow(saturate(dot(n, H)), 16) * 0.05;
                color = MixFog(color, InitializeInputDataFog(float4(p, 1), input.fogFactor));
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 pos, nrm;
                Wave(input.positionOS.xyz, input.uv, pos, nrm);
                float3 positionWS = TransformObjectToWorld(pos);
                float3 normalWS = TransformObjectToWorldNormal(nrm);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return o;
            }
            half frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 pos, nrm;
                Wave(input.positionOS.xyz, input.uv, pos, nrm);
                o.positionCS = TransformObjectToHClip(pos);
                return o;
            }
            half frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
