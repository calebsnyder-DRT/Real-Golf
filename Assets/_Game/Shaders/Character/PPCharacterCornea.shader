Shader "Puttshack/Character/Cornea"
{
    // Clear wet shell over the eyeball: reflections and highlights only, premultiplied over the iris and sclera.
    // A small camera-relative catchlight keeps the eyes alive in flat light.
    Properties
    {
        _Smoothness("Smoothness", Range(0, 1)) = 0.96
        _Catchlight("Catchlight", Range(0, 4)) = 1.2
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _BaseMap("Unused", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CorneaVertex
            #pragma fragment CorneaFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "PPCharacterInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings CorneaVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(v.positionCS.z);
                return o;
            }

            half4 CorneaFragment(Varyings input) : SV_Target
            {
                half3 N = normalize(input.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half NoV = saturate(dot(N, V));
                half F = 0.04h + 0.96h * pow(1.0h - NoV, 5.0h);
                half perceptual = 1.0h - _Smoothness;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 env = GlossyEnvironmentReflection(reflect(-V, N), input.positionWS, perceptual, 1.0h, screenUV);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 H = SafeNormalize(light.direction + V);
                half r = max(perceptual * perceptual, 0.002h);
                half r2 = r * r;
                half NoH = saturate(dot(N, H));
                half d = NoH * NoH * (r2 - 1.0h) + 1.0001h;
                half LoH = saturate(dot(light.direction, H));
                half spec = r2 / (d * d * max(0.1h, LoH * LoH) * (r * 4.0h + 2.0h));
                spec = min(spec, 64.0h);
                half3 direct = light.color * spec * saturate(dot(N, light.direction)) * light.shadowAttenuation * light.distanceAttenuation * 0.04h;

                // catchlight from a soft key just above and left of the camera
                half3 keyVS = normalize(half3(-0.35h, 0.45h, 1.0h));
                half3 keyWS = normalize(mul((float3x3)UNITY_MATRIX_I_V, keyVS));
                half3 Hk = SafeNormalize(keyWS + V);
                half catchlight = pow(saturate(dot(N, Hk)), 900.0h) * _Catchlight;

                half3 color = (env * F + direct + catchlight.xxx) * _BaseColor.rgb;
                color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                return half4(color, saturate(F + catchlight * 0.5h));
            }
            ENDHLSL
        }
    }
}
