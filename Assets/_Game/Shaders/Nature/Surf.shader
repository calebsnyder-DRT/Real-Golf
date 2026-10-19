// Breaking surf along the Pebble Beach shoreline and round the sea stacks (strips from ArtSource/Blender/scripts/pp_coast.py).
// UV0: x = metres along the strip, y = 0 at the waterline to 1 at the seaward edge. Vertex colour: R = surf strength,
// G = rock splash (broken, lacier foam), B = wave phase offset, A = strip-end fade. Whitewater bands roll shoreward and
// break up in the foam noise, a lacy swash hugs the waterline, and the scene depth (the ocean writes depth only after
// the opaque copy) thickens foam in the shallows and softens the contact with rocks and sand. Draws after the ocean.
Shader "Puttshack/Surf"
{
    Properties
    {
        _FoamTex ("Foam Noise", 2D) = "white" {}
        _FoamColor ("Foam", Color) = (0.95, 0.96, 0.94, 1)
        _FoamScale ("Foam Scale (1/m)", Float) = 0.16
        _BandCount ("Wave Bands Across Strip", Float) = 3
        _WaveSpeed ("Wave Speed (bands/s)", Float) = 0.11
        _CrestSharpness ("Crest Sharpness", Range(0.02, 0.3)) = 0.07
        _SwashWidth ("Swash Width (strip fraction)", Range(0, 0.6)) = 0.22
        _ContactFade ("Contact Fade (m)", Float) = 0.3
        _ShallowReach ("Shallow Foam Reach (m)", Float) = 3
        _Opacity ("Opacity", Range(0, 1)) = 0.92
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-40" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FoamTex_ST;
                half4 _FoamColor;
                float _FoamScale;
                float _BandCount;
                float _WaveSpeed;
                half _CrestSharpness;
                half _SwashWidth;
                float _ContactFade;
                float _ShallowReach;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.uv = input.uv;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionWS;
                float v = input.uv.y;
                float t = _Time.y;
                half4 c = input.color;

                // Whitewater bands: a sharp shoreward front with a long trailing tail, rolling toward the waterline.
                float phase = frac(v * _BandCount + t * _WaveSpeed + c.b);
                half crest = smoothstep(0, _CrestSharpness, phase) * (1 - smoothstep(_CrestSharpness, 0.65, phase));
                float2 q = p.xz * _FoamScale;
                half n1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, q + float2(t * 0.013, -t * 0.021)).r;
                half n2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, q * 2.7 + float2(-t * 0.031, t * 0.017) + n1 * 0.15).r;
                half lace = n1 * 0.6 + n2 * 0.4;
                half breakup = lerp(0.42, 0.6, c.g);
                half bands = crest * smoothstep(breakup - 0.32 * crest, breakup + 0.25, lace);

                // Swash: persistent lacy foam at the waterline that surges with each set.
                half surge = 0.6 + 0.4 * sin(t * 0.55 + input.uv.x * 0.045 + c.b * 6.283);
                half swash = (1 - smoothstep(0, _SwashWidth * surge, v)) * smoothstep(0.3, 0.7, n2 + n1 * 0.25);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float depth = max(0, sceneEye - surfEye);
                half contact = saturate(depth / max(_ContactFade, 1e-3));
                half shallow = 1 - saturate(depth / max(_ShallowReach, 1e-3));

                half foam = max(bands * (0.35 + 0.65 * shallow), swash) * c.r;
                half edges = (1 - smoothstep(0.75, 1, v)) * c.a;
                half alpha = saturate(foam * contact * edges * _Opacity);
                clip(alpha - 0.003);

                Light light = GetMainLight(TransformWorldToShadowCoord(p), p, half4(1, 1, 1, 1));
                half shadow = lerp(0.35, 1, light.shadowAttenuation);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 color = _FoamColor.rgb * (ambient + light.color * saturate(light.direction.y * 0.8 + 0.2) * shadow);
                color = MixFog(color, InitializeInputDataFog(float4(p, 1), input.fogFactor));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
