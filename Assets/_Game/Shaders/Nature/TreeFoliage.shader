// Course trees from ArtSource/Blender/scripts/pp_nature.py: alpha-cut leaf cards, bark and impostor cards share this
// shader. Vertex colours: R = occlusion, G = sun-bleached tip tint, B = sway weight (reach from the trunk, so bark and
// leaves at a limb end move together), A = leaf flutter. Card normals are volumetric (from the canopy pad centres), so
// both faces are lit alike and the canopy shades as soft masses; the low sun glows through backlit edges.
// Bark uses _Cutoff 0 and Cull Back; impostors use _OcclusionStrength 0 (their occlusion is baked into the atlas).
Shader "Puttshack/TreeFoliage"
{
    Properties
    {
        _BaseMap ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _TipTint ("Tip Tint (x vertex G)", Color) = (1, 0.96, 0.72, 1)
        _TipStrength ("Tip Tint Strength", Range(0, 2)) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _OcclusionStrength ("Vertex Occlusion", Range(0, 1)) = 0.8
        _NormalUp ("Normal Up Bias", Range(0, 1)) = 0.15
        _Wrap ("Light Wrap", Range(0, 1)) = 0.45
        _TranslucencyColor ("Translucency", Color) = (0.62, 0.66, 0.25, 1)
        _Translucency ("Translucency Strength", Range(0, 3)) = 0.9
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.12
        _WindDirection ("Wind Direction (xz)", Vector) = (1, 0, 0.35, 0)
        _WindStrength ("Wind Sway (m at full weight)", Range(0, 1)) = 0.22
        _WindSpeed ("Wind Speed", Float) = 0.9
        _Flutter ("Leaf Flutter (m)", Range(0, 0.2)) = 0.035
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BumpMap_ST;
            half4 _BaseColor;
            half4 _TipTint;
            half _TipStrength;
            half _BumpScale;
            half _Cutoff;
            half _OcclusionStrength;
            half _NormalUp;
            half _Wrap;
            half4 _TranslucencyColor;
            half _Translucency;
            half _Smoothness;
            half _SpecularStrength;
            float4 _WindDirection;
            float _WindStrength;
            float _WindSpeed;
            float _Flutter;
            float _Cull;
        CBUFFER_END

        // Whole-limb sway (weight B) as a slow travelling gust, plus a fast per-leaf flutter (weight A). The phase is
        // taken from the world position at low frequency, so merged scenery meshes still move tree by tree.
        float3 TreeWindAt(float3 positionWS, half4 color, float time)
        {
            float2 dir = normalize(_WindDirection.xz + 1e-5);
            float t = time * _WindSpeed;
            float phase = dot(positionWS.xz, float2(0.045, 0.031));
            float gust = 0.55 + 0.45 * sin(t * 0.31 + dot(positionWS.xz, dir) * 0.013);
            float sway = (sin(t + phase) * 0.65 + sin(t * 2.13 + phase * 1.7) * 0.35) * gust;
            float w = _WindStrength * color.b;
            float3 offset = float3(dir.x, 0, dir.y) * (sway + 0.35) * w;
            offset.y -= abs(sway) * w * 0.12;
            float flutter = sin(t * 6.3 + dot(positionWS, float3(1.9, 2.7, 1.3))) * _Flutter * color.a * (0.6 + 0.4 * gust);
            return offset + float3(flutter * 0.5, flutter, flutter * 0.4);
        }

        float3 TreeWind(float3 positionWS, half4 color) { return TreeWindAt(positionWS, color, _Time.y); }

        half SampleCutout(float2 uv)
        {
            half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
            clip(a - _Cutoff);
            return a;
        }
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
            #include "../CourseGI.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 color : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += TreeWind(positionWS, input.color);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = n.normalWS;
                output.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a * _BaseColor.a - _Cutoff);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                float3 p = input.positionWS;
                half3 V = GetWorldSpaceNormalizeViewDir(p);
                half3 N = normalize(input.normalWS);
                if (_BumpScale > 0.001)
                {
                    half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                    half3 T = normalize(input.tangentWS.xyz);
                    half3 B = cross(N, T) * input.tangentWS.w;
                    N = normalize(tn.x * T + tn.y * B + tn.z * N);
                }
                N = normalize(lerp(N, half3(0, 1, 0), _NormalUp));

                half ao = lerp(1, input.color.r, _OcclusionStrength);
                half3 tint = lerp(half3(1, 1, 1), _TipTint.rgb, saturate(input.color.g * _TipStrength));
                half3 albedo = tex.rgb * _BaseColor.rgb * tint * ao;

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
                half3 direct = albedo * light.color * wrap * shadow * ssaoDirect;

                half back = pow(saturate(dot(-V, light.direction)), 4);
                half thin = saturate(input.color.a * 0.7 + 0.3) * (1 - input.color.r * 0.35);
                half3 trans = _TranslucencyColor.rgb * tex.rgb * 2.2 * light.color * back * _Translucency * thin
                    * lerp(0.25, 1, shadow) * ao;

                half3 H = normalize(light.direction + V);
                half specPow = exp2(10 * _Smoothness + 1);
                half3 spec = light.color * pow(saturate(dot(N, H)), specPow) * _SpecularStrength * shadow * ao;

                half3 ambient = CourseBakedGI(p, N, V, input.positionCS.xy) * albedo * ssaoIndirect;
                half3 color = ambient + direct + trans + spec;
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += TreeWind(positionWS, input.color);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                SampleCutout(input.uv);
                return 0;
            }
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

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS + TreeWind(positionWS, input.color));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SampleCutout(input.uv);
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS + TreeWind(positionWS, input.color));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SampleCutout(input.uv);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }

        // Per-object motion vectors including the sway at _LastTimeParameters, so TAA reprojects moving canopies.
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

            struct Attributes { float4 positionOS : POSITION; float3 positionOld : TEXCOORD4; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionCSNoJitter : TEXCOORD0;
                float4 previousPositionCSNoJitter : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += TreeWind(positionWS, input.color);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(positionWS, 1));
                float4 previousOS = unity_MotionVectorsParams.x == 1 ? float4(input.positionOld, 1) : input.positionOS;
                float3 previousWS = mul(UNITY_PREV_MATRIX_M, previousOS).xyz;
                previousWS += TreeWindAt(previousWS, input.color, _LastTimeParameters.x);
                output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(previousWS, 1));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SampleCutout(input.uv);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                return float4(CalcNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 0, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
