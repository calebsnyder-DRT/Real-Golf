// Cart path concrete ribbon (CourseVisualBuilder.BuildCartPathMesh). UV.x runs -1..1 across the slab, UV.y is metres
// along the course loop; vertex alpha is 1 on the concrete and 0 on the worn verge where it fades into the turf.
// Broom finish, saw-cut joints, tyre wear down the middle, sand and grass creeping over the edges.
Shader "Puttshack/CartPath"
{
    Properties
    {
        _BaseColor ("Concrete", Color) = (0.66, 0.64, 0.6, 1)
        _WearColor ("Tyre Wear", Color) = (0.5, 0.49, 0.47, 1)
        _VergeColor ("Verge", Color) = (0.42, 0.45, 0.27, 1)
        _NoiseTex ("Speckle Noise", 2D) = "gray" {}
        _JointSpacing ("Joint Spacing (m)", Float) = 3.2
        _Smoothness ("Smoothness", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _NoiseTex_ST;
            half4 _BaseColor;
            half4 _WearColor;
            half4 _VergeColor;
            float _JointSpacing;
            half _Smoothness;
        CBUFFER_END
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CourseGI.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half verge : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.verge = input.color.a;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionWS;
                half3 n = normalize(input.normalWS);
                float across = input.uv.x;
                float along = input.uv.y;

                half speck = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xz * 0.9).g;
                half blot = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xz * 0.06 + 0.31).g;
                half3 albedo = _BaseColor.rgb * (0.9 + 0.2 * speck) * (0.92 + 0.16 * blot);
                // Two tyre tracks worn darker and smoother.
                half track = saturate(1 - abs(abs(across) - 0.5) * 4.5);
                albedo = lerp(albedo, _WearColor.rgb * (0.9 + 0.2 * speck), track * 0.45);
                // Saw-cut joints across the slab.
                float j = along / _JointSpacing;
                float jw = max(fwidth(j), 1e-4);
                half joint = 1 - smoothstep(0.004, 0.004 + jw * 1.5, abs(frac(j + 0.5) - 0.5));
                albedo *= 1 - 0.45 * joint;
                // Dirt and sand along the edges; the verge fades to grass.
                half edge = smoothstep(0.75, 1.0, abs(across));
                albedo = lerp(albedo, albedo * half3(0.85, 0.8, 0.7), edge * 0.6);
                half verge = saturate(input.verge);
                albedo = lerp(_VergeColor.rgb * (0.85 + 0.3 * speck), albedo, smoothstep(0.35, 0.95, verge + (blot - 0.5) * 0.3));

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(p);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(p, 1), input.fogFactor);
                inputData.bakedGI = CourseBakedGI(p, n, inputData.viewDirectionWS, input.positionCS.xy);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = saturate(albedo);
                s.smoothness = lerp(_Smoothness * 0.5, _Smoothness + track * 0.08, verge);
                s.normalTS = half3(0, 0, 1);
                s.occlusion = 1 - joint * 0.5;
                s.alpha = 1;
                half4 color = UniversalFragmentPBR(inputData, s);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input) { Varyings o; UNITY_SETUP_INSTANCE_ID(input); o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o; }
            half frag(Varyings input) : SV_Target { return 0; }
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 frag(Varyings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0); }
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
            struct Attributes { float4 positionOS : POSITION; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input) { Varyings o; o.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2); return o; }
            half4 frag(Varyings input) : SV_Target
            {
                UnityMetaInput meta = (UnityMetaInput)0;
                meta.Albedo = _BaseColor.rgb;
                return UnityMetaFragment(meta);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
