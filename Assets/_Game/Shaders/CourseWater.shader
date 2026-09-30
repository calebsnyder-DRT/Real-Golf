// Simple readable water: depth-free tint with a gentle animated sheen. Visual only; the hazard volume is authoritative.
Shader "Puttshack/CourseWater"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.30, 0.62, 0.70, 1)
        _DeepColor ("Deep", Color) = (0.10, 0.34, 0.46, 1)
        _SheenStrength ("Sheen", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _SheenStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half edge : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.edge = input.color.r;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, input.edge);
                float t = _Time.y;
                float2 p = input.positionWS.xz;
                float2 slope =
                    float2(0.83, 0.56) * cos(dot(p, float2(0.83, 0.56)) * 1.7 + t * 1.3) * 0.5 +
                    float2(-0.44, 0.9) * cos(dot(p, float2(-0.44, 0.9)) * 2.9 - t * 1.7) * 0.3 +
                    float2(0.21, -0.98) * cos(dot(p, float2(0.21, -0.98)) * 5.3 + t * 2.3) * 0.15;
                float3 normalWS = normalize(float3(-slope.x * 0.12, 1, -slope.y * 0.12));
                Light mainLight = GetMainLight();
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half spec = pow(saturate(dot(normalize(mainLight.direction + viewDir), normalWS)), 96);
                half fresnel = pow(1 - saturate(dot(viewDir, normalWS)), 4);
                color = lerp(color, _ShallowColor.rgb * 1.15, fresnel * 0.35);
                color += spec * _SheenStrength * mainLight.color;
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
