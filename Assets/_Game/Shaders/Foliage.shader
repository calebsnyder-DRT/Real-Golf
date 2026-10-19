// Monterey cypress and coastal forest foliage. The canopy pads are solid clumps (ArtSource/Blender/scripts/pp_pebble.py)
// whose vertex colours carry R = occlusion (pad undersides, inner and lower pads) and G = tint (sun-bleached tips).
// Leaf-cluster relief comes from a tileable clump normal/height pair sampled triplanar in world space; light wraps
// around the pads and glows through their edges when the low sun is behind them.
Shader "Puttshack/Foliage"
{
    Properties
    {
        _BaseColor ("Shade Green", Color) = (0.085, 0.13, 0.065, 1)
        _TipColor ("Tip Green", Color) = (0.21, 0.29, 0.12, 1)
        _ClumpNormal ("Clump Normal", 2D) = "bump" {}
        _ClumpNoise ("Clump Height", 2D) = "gray" {}
        _ClumpScale ("Clump Scale (1/m)", Float) = 0.45
        _ClumpStrength ("Clump Normal Strength", Range(0, 2)) = 1.4
        _OcclusionStrength ("Vertex Occlusion", Range(0, 1)) = 0.85
        _CreviceStrength ("Clump Crevices", Range(0, 1)) = 0.65
        _Wrap ("Light Wrap", Range(0, 1)) = 0.35
        _TranslucencyColor ("Translucency", Color) = (0.45, 0.55, 0.15, 1)
        _Translucency ("Translucency Strength", Range(0, 2)) = 0.6
        _Sheen ("Sheen", Range(0, 0.2)) = 0.035
        _WindDirection ("Wind Direction (xz)", Vector) = (1, 0, 0.35, 0)
        _WindStrength ("Wind Sway (m)", Range(0, 0.4)) = 0.09
        _WindSpeed ("Wind Speed", Float) = 1.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _ClumpNormal_ST;
            float4 _ClumpNoise_ST;
            half4 _BaseColor;
            half4 _TipColor;
            float _ClumpScale;
            half _ClumpStrength;
            half _OcclusionStrength;
            half _CreviceStrength;
            half _Wrap;
            half4 _TranslucencyColor;
            half _Translucency;
            half _Sheen;
            float4 _WindDirection;
            float _WindStrength;
            float _WindSpeed;
        CBUFFER_END

        // Canopy sway: outer, sun-tipped pads (vertex colour R = exposure, G = tip) move most; gusts roll across the
        // course as a travelling wave so neighbouring trees move together but not in lockstep.
        float3 SwayAt(float3 positionWS, half4 color, float time)
        {
            float2 dir = normalize(_WindDirection.xz + 1e-5);
            float phase = dot(positionWS.xz, dir) * 0.045 + dot(positionWS.xz, float2(0.11, 0.07));
            float t = time * _WindSpeed;
            float gust = 0.6 + 0.4 * sin(t * 0.37 + dot(positionWS.xz, dir) * 0.012);
            float sway = (sin(t + phase) * 0.7 + sin(t * 2.3 + phase * 1.9) * 0.3) * gust;
            float flutter = sin(t * 7.0 + dot(positionWS, float3(1.7, 2.3, 1.3))) * 0.12;
            float w = _WindStrength * saturate(color.r) * (0.35 + 0.65 * saturate(color.g));
            return float3(dir.x, 0, dir.y) * (sway + flutter) * w + float3(0, flutter * w * 0.4, 0);
        }

        float3 Sway(float3 positionWS, half4 color) { return SwayAt(positionWS, color, _Time.y); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #include "CourseGI.hlsl"

            TEXTURE2D(_ClumpNormal); SAMPLER(sampler_ClumpNormal);
            TEXTURE2D(_ClumpNoise);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + Sway(TransformObjectToWorld(input.positionOS.xyz), input.color);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                float3 p = input.positionWS;
                half3 n = normalize(input.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(p);

                half3 blend = pow(abs(n), 4);
                blend /= dot(blend, 1);
                float3 q = p * _ClumpScale;
                half hx = SAMPLE_TEXTURE2D(_ClumpNoise, sampler_ClumpNormal, q.zy).r;
                half hy = SAMPLE_TEXTURE2D(_ClumpNoise, sampler_ClumpNormal, q.xz).r;
                half hz = SAMPLE_TEXTURE2D(_ClumpNoise, sampler_ClumpNormal, q.xy).r;
                half h = hx * blend.x + hy * blend.y + hz * blend.z;

                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_ClumpNormal, sampler_ClumpNormal, q.zy), _ClumpStrength);
                half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_ClumpNormal, sampler_ClumpNormal, q.xz), _ClumpStrength);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_ClumpNormal, sampler_ClumpNormal, q.xy), _ClumpStrength);
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                half3 N = normalize(tnX.zyx * blend.x + tnY.xzy * blend.y + tnZ.xyz * blend.z);

                half vertexAO = lerp(1, input.color.r, _OcclusionStrength);
                half crevice = lerp(1 - _CreviceStrength, 1, smoothstep(0.15, 0.75, h));
                half ao = vertexAO * crevice;
                half tip = saturate(input.color.g * 0.7 + (h - 0.5) * 0.4 + n.y * 0.1);
                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, tip);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half ssaoIndirect = 1, ssaoDirect = 1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ssaoIndirect = ssao.indirectAmbientOcclusion;
                    ssaoDirect = ssao.directAmbientOcclusion;
                #endif

                Light light = GetMainLight(TransformWorldToShadowCoord(p), p, half4(1, 1, 1, 1));
                half shadow = light.shadowAttenuation * light.distanceAttenuation;
                half ndl = dot(N, light.direction);
                half wrap = saturate((ndl + _Wrap) / (1 + _Wrap));
                half3 direct = albedo * light.color * wrap * shadow * lerp(1, ao, 0.5) * ssaoDirect;

                // Backlit pads: the low sun shows through the thin outer leaves, strongest at the rims.
                half back = pow(saturate(dot(-V, light.direction)), 5);
                half rim = 1 - saturate(dot(n, V));
                half3 trans = _TranslucencyColor.rgb * light.color * back * (0.35 + 0.65 * rim) * _Translucency
                    * lerp(0.4, 1, shadow) * input.color.r;

                half3 H = normalize(light.direction + V);
                half3 sheen = light.color * pow(saturate(dot(N, H)), 24) * _Sheen * shadow * ao;

                half3 ambient = CourseBakedGI(p, N, V, input.positionCS.xy) * albedo * ao * ssaoIndirect;
                half3 color = ambient + direct + trans + sheen;
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

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += Sway(positionWS, input.color);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS + Sway(positionWS, input.color));
                return output;
            }

            half frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS + Sway(positionWS, input.color));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }

        // Per-object motion vectors that include the wind sway (previous frame's sway at _LastTimeParameters), so
        // temporal AA reprojects swaying canopies instead of smearing them. Renderers need Motion Vectors = Per Object.
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 positionOld : TEXCOORD4; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionCSNoJitter : TEXCOORD0;
                float4 previousPositionCSNoJitter : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += Sway(positionWS, input.color);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1));
                float4 previousOS = unity_MotionVectorsParams.x == 1 ? float4(input.positionOld, 1) : input.positionOS;
                float3 previousWS = mul(UNITY_PREV_MATRIX_M, previousOS).xyz;
                previousWS += SwayAt(previousWS, input.color, _LastTimeParameters.x);
                output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(previousWS, 1));
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                return float4(CalcNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UnityMetaInput meta = (UnityMetaInput)0;
                meta.Albedo = lerp(_BaseColor.rgb, _TipColor.rgb, saturate(input.color.g * 0.7)) * lerp(1, input.color.r, _OcclusionStrength);
                return UnityMetaFragment(meta);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
