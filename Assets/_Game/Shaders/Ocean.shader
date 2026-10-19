// Stillwater Cove sea surface. Water colour and clarity come from the depth of the terrain under it (camera depth
// texture): turquoise and see-through over the sand, deep blue offshore, foam at the waterline. Animated normal maps,
// Fresnel reflection of the sky/reflection probe and a low-sun glint. Visual only; the hazard volume is authoritative.
Shader "Puttshack/Ocean"
{
    Properties
    {
        _WaveNormal ("Wave Normal", 2D) = "bump" {}
        _FoamTex ("Foam Noise", 2D) = "white" {}
        _ShallowColor ("Shallow", Color) = (0.2, 0.68, 0.66, 1)
        _MidColor ("Mid", Color) = (0.05, 0.38, 0.48, 1)
        _DeepColor ("Deep", Color) = (0.02, 0.15, 0.27, 1)
        _FoamColor ("Foam", Color) = (0.95, 0.96, 0.94, 1)
        _DepthRange ("Depth To Deep (m)", Float) = 9
        _Clarity ("Clarity Depth (m)", Float) = 2.2
        _WaveScaleA ("Wave Scale A (1/m)", Float) = 0.055
        _WaveScaleB ("Wave Scale B (1/m)", Float) = 0.16
        _SwellScale ("Swell Scale (1/m)", Float) = 0.011
        _WaveSpeed ("Wave Speed", Float) = 0.035
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.75
        _FarFade ("Detail Fade Distance (m)", Float) = 700
        _SunSharpness ("Sun Glint Sharpness", Float) = 900
        _SunIntensity ("Sun Glint Intensity", Float) = 6
        _ReflectionStrength ("Reflection Strength", Range(0, 1.5)) = 1
        _ReflectionTint ("Reflection Tint", Color) = (0.62, 0.8, 0.92, 1)
        _FresnelMax ("Fresnel Max", Range(0, 1)) = 0.4
        _ShoreFoamDepth ("Shore Foam Depth (m)", Float) = 0.7
        _FoamScale ("Foam Scale (1/m)", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _WaveNormal_ST;
                float4 _FoamTex_ST;
                half4 _ShallowColor;
                half4 _MidColor;
                half4 _DeepColor;
                half4 _FoamColor;
                float _DepthRange;
                float _Clarity;
                float _WaveScaleA;
                float _WaveScaleB;
                float _SwellScale;
                float _WaveSpeed;
                half _NormalStrength;
                float _FarFade;
                float _SunSharpness;
                half _SunIntensity;
                half _ReflectionStrength;
                half4 _ReflectionTint;
                half _FresnelMax;
                float _ShoreFoamDepth;
                float _FoamScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half3 SampleWave(float2 uv)
            {
                return UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, uv));
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionWS;
                float3 toCam = _WorldSpaceCameraPos - p;
                float dist = length(toCam);
                half3 V = toCam / max(dist, 1e-4);
                float t = _Time.y * _WaveSpeed;

                half3 a = SampleWave(p.xz * _WaveScaleA + t * float2(1.0, 0.35));
                half3 b = SampleWave(p.xz * _WaveScaleB + t * float2(-0.55, 1.0) * 1.9);
                half3 s = SampleWave(p.xz * _SwellScale + t * float2(0.2, 0.12) * 0.6);
                half detail = saturate(1 - dist / _FarFade);
                half2 slope = (a.xy * 0.55 + b.xy * 0.45 * detail) * (0.35 + 0.65 * detail) + s.xy * 0.7;
                half3 n = normalize(half3(slope.x * _NormalStrength, 1, slope.y * _NormalStrength));

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float thickness = max(0, sceneEye - waterEye);
                float depth = thickness * max(V.y, 0.08);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(p), p, half4(1, 1, 1, 1));
                half shadow = lerp(0.55, 1, mainLight.shadowAttenuation);

                half depth01 = saturate(depth / _DepthRange);
                half3 body = lerp(_ShallowColor.rgb, _MidColor.rgb, saturate(depth01 * 2.2));
                body = lerp(body, _DeepColor.rgb, saturate(depth01 * 1.6 - 0.45));
                body = lerp(body, _DeepColor.rgb, saturate(dist / 1800));
                half3 ambient = SampleSH(half3(0, 1, 0));
                half ndl = saturate(mainLight.direction.y);
                body *= ambient * 0.9 + mainLight.color * ndl * 0.55 * shadow;

                // Capped so the grazing view from the clifftop still reads as blue water rather than a mirror of the haze.
                half fresnel = 0.02 + (_FresnelMax - 0.02) * pow(1 - saturate(dot(n, V)), 5);
                half3 R = reflect(-V, n);
                R.y = abs(R.y);
                half3 reflection = GlossyEnvironmentReflection(R, p, 0.05, 1, screenUV) * _ReflectionStrength * _ReflectionTint.rgb;

                half3 H = normalize(mainLight.direction + V);
                half nh = saturate(dot(n, H));
                half glint = pow(nh, _SunSharpness) * _SunIntensity + pow(nh, _SunSharpness * 0.06) * 0.25;
                half3 spec = glint * mainLight.color * shadow * fresnel * 6;

                half foamNoise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, p.xz * _FoamScale + a.xy * 0.08 + t * 0.6).r;
                half shore = saturate(1 - depth / _ShoreFoamDepth);
                half pulse = 0.55 + 0.45 * sin(_Time.y * 0.9 + p.x * 0.07 + p.z * 0.05);
                half foam = saturate(smoothstep(1 - shore * pulse, 1.02 - shore * pulse * 0.6, foamNoise) + smoothstep(0.82, 1, shore) * 0.85);
                foam *= detail;

                half3 color = lerp(body, reflection, fresnel) + spec;
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * ndl * shadow);
                color = lerp(color, foamLit, foam);

                half alpha = saturate(depth / _Clarity);
                alpha = saturate(max(max(alpha, fresnel), foam) + saturate(dist / 300));
                color = MixFog(color, InitializeInputDataFog(float4(p, 1), input.fogFactor));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
