// Course ground: vertex color carries surface weights (R fairway/tee, G green, B bunker, A deep rough;
// the remainder is light rough) so the rendered lie matches the collision surface map exactly.
Shader "Puttshack/CourseGround"
{
    Properties
    {
        _RoughTex ("Light Rough", 2D) = "white" {}
        _FairwayTex ("Fairway", 2D) = "white" {}
        _GreenTex ("Green", 2D) = "white" {}
        _BunkerTex ("Bunker", 2D) = "white" {}
        _DeepRoughTex ("Deep Rough", 2D) = "white" {}
        _RoughTint ("Light Rough Tint", Color) = (0.36, 0.55, 0.22, 1)
        _FairwayTint ("Fairway Tint", Color) = (0.45, 0.70, 0.28, 1)
        _GreenTint ("Green Tint", Color) = (0.42, 0.74, 0.30, 1)
        _BunkerTint ("Bunker Tint", Color) = (0.93, 0.86, 0.66, 1)
        _DeepRoughTint ("Deep Rough Tint", Color) = (0.25, 0.42, 0.16, 1)
        _Tiling ("World Tiling (1/m)", Float) = 0.12
        _TextureStrength ("Texture Strength", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RoughTex); SAMPLER(sampler_RoughTex);
            TEXTURE2D(_FairwayTex); SAMPLER(sampler_FairwayTex);
            TEXTURE2D(_GreenTex); SAMPLER(sampler_GreenTex);
            TEXTURE2D(_BunkerTex); SAMPLER(sampler_BunkerTex);
            TEXTURE2D(_DeepRoughTex); SAMPLER(sampler_DeepRoughTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _RoughTex_ST;
                float4 _FairwayTex_ST;
                float4 _GreenTex_ST;
                float4 _BunkerTex_ST;
                float4 _DeepRoughTex_ST;
                half4 _RoughTint;
                half4 _FairwayTint;
                half4 _GreenTint;
                half4 _BunkerTint;
                half4 _DeepRoughTint;
                float _Tiling;
                half _TextureStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 weights : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.weights = input.color;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half3 Layer(TEXTURE2D_PARAM(tex, samp), float2 uv, half4 tint)
            {
                half3 albedo = SAMPLE_TEXTURE2D(tex, samp, uv).rgb;
                return tint.rgb * lerp(half3(1, 1, 1), albedo * 1.6, _TextureStrength);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionWS.xz * _Tiling;
                half4 w = saturate(input.weights);
                half rough = saturate(1.0 - w.r - w.g - w.b - w.a);

                half3 albedo = Layer(TEXTURE2D_ARGS(_RoughTex, sampler_RoughTex), uv, _RoughTint) * rough
                    + Layer(TEXTURE2D_ARGS(_FairwayTex, sampler_FairwayTex), uv * 1.5, _FairwayTint) * w.r
                    + Layer(TEXTURE2D_ARGS(_GreenTex, sampler_GreenTex), uv * 2.0, _GreenTint) * w.g
                    + Layer(TEXTURE2D_ARGS(_BunkerTex, sampler_BunkerTex), uv * 1.2, _BunkerTint) * w.b
                    + Layer(TEXTURE2D_ARGS(_DeepRoughTex, sampler_DeepRoughTex), uv, _DeepRoughTint) * w.a;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 n = normalize(input.normalWS);
                half ndl = saturate(dot(n, mainLight.direction));
                half3 lighting = SampleSH(n) + mainLight.color * ndl * mainLight.shadowAttenuation;
                half3 color = MixFog(albedo * lighting, input.fogFactor);
                return half4(color, 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
