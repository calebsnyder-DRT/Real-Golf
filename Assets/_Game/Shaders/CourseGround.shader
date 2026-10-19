// Course ground. With _UseMasks the outlines come from per-vertex signed distances to the gameplay polygons
// (UV1 = green, bunker; UV2 = fairway/tee, deep rough; meters, negative inside), so edges are crisp and exactly where
// the solver classifies the lie. Without masks (scenery terrain) the vertex colour carries surface weights instead
// (R fairway/tee, G green, B bunker, A deep rough; the remainder is light rough). UV0 holds mowing-stripe coordinates
// (along, across the line of play). Steep faces turn to cliff rock and ground below the beach line to sand, which is
// how the coastal penalty area reads; the sea itself is drawn by Puttshack/Ocean.
Shader "Puttshack/CourseGround"
{
    Properties
    {
        _RoughTex ("Light Rough", 2D) = "white" {}
        _FairwayTex ("Fairway", 2D) = "white" {}
        _GreenTex ("Green", 2D) = "white" {}
        _BunkerTex ("Bunker", 2D) = "white" {}
        _DeepRoughTex ("Deep Rough", 2D) = "white" {}
        _TurfNormal ("Turf Normal", 2D) = "bump" {}
        _SandNormal ("Sand Normal", 2D) = "bump" {}
        _RockTex ("Cliff Rock", 2D) = "white" {}
        _RockNormal ("Cliff Rock Normal", 2D) = "bump" {}
        _BeachTex ("Beach Sand", 2D) = "white" {}
        _BeachNormal ("Beach Normal", 2D) = "bump" {}

        _RoughTint ("Light Rough Tint", Color) = (0.36, 0.52, 0.2, 1)
        _FairwayTint ("Fairway Tint", Color) = (0.42, 0.66, 0.24, 1)
        _CollarTint ("Collar Tint", Color) = (0.45, 0.7, 0.27, 1)
        _GreenTint ("Green Tint", Color) = (0.36, 0.66, 0.25, 1)
        _BunkerTint ("Bunker Tint", Color) = (0.96, 0.92, 0.82, 1)
        _DeepRoughTint ("Deep Rough Tint", Color) = (0.36, 0.42, 0.2, 1)
        _ScrubTint ("Coastal Scrub Accent", Color) = (0.52, 0.3, 0.22, 1)
        _RockTint ("Rock Tint", Color) = (0.6, 0.57, 0.53, 1)
        _BeachTint ("Beach Tint", Color) = (0.93, 0.84, 0.68, 1)
        _WetSandTint ("Wet Sand Tint", Color) = (0.55, 0.47, 0.37, 1)

        _UseMasks ("Use Distance Masks", Float) = 1
        _Tiling ("World Tiling (1/m)", Float) = 0.12
        _TextureStrength ("Texture Strength", Range(0, 1)) = 0.55
        _MacroStrength ("Macro Variation", Range(0, 1)) = 0.35
        _StripeWidth ("Fairway Stripe Width (m)", Float) = 5.5
        _StripeStrength ("Fairway Stripe Strength", Range(0, 0.3)) = 0.09
        _GreenStripeWidth ("Green Stripe Width (m)", Float) = 1.8
        _GreenStripeStrength ("Green Stripe Strength", Range(0, 0.3)) = 0.05
        _StripeViewGain ("Stripe View Dependence", Range(0, 4)) = 1.8
        _FirstCutTint ("First Cut Tint", Color) = (0.4, 0.6, 0.23, 1)
        _FirstCutWidth ("First Cut Width (m)", Float) = 1.8
        _DryTint ("Dry Patch Tint", Color) = (0.62, 0.55, 0.3, 1)
        _DryStrength ("Dry Patches", Range(0, 1)) = 0.35
        _GrassSheen ("Backlit Grass Sheen", Range(0, 1)) = 0.35
        _RakeStrength ("Bunker Rake Lines", Range(0, 0.2)) = 0.07
        _NormalStrength ("Turf Normal Strength", Range(0, 2)) = 0.6
        _RockScale ("Rock Scale (1/m)", Float) = 0.11
        _RockSlope ("Rock Below Normal Y", Range(0, 1)) = 0.74
        _BeachTop ("Beach Top Height", Float) = -7.4
        _SeaLevel ("Sea Level", Float) = -10
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _RoughTex_ST;
            float4 _FairwayTex_ST;
            float4 _GreenTex_ST;
            float4 _BunkerTex_ST;
            float4 _DeepRoughTex_ST;
            half4 _RoughTint;
            half4 _FairwayTint;
            half4 _CollarTint;
            half4 _GreenTint;
            half4 _BunkerTint;
            half4 _DeepRoughTint;
            half4 _ScrubTint;
            half4 _RockTint;
            half4 _BeachTint;
            half4 _WetSandTint;
            float _UseMasks;
            float _Tiling;
            half _TextureStrength;
            half _MacroStrength;
            float _StripeWidth;
            half _StripeStrength;
            float _GreenStripeWidth;
            half _GreenStripeStrength;
            half _StripeViewGain;
            half4 _FirstCutTint;
            float _FirstCutWidth;
            half4 _DryTint;
            half _DryStrength;
            half _GrassSheen;
            half _RakeStrength;
            half _NormalStrength;
            float _RockScale;
            half _RockSlope;
            float _BeachTop;
            float _SeaLevel;
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
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CourseGI.hlsl"

            TEXTURE2D(_RoughTex); SAMPLER(sampler_RoughTex);
            TEXTURE2D(_FairwayTex);
            TEXTURE2D(_GreenTex);
            TEXTURE2D(_BunkerTex);
            TEXTURE2D(_DeepRoughTex);
            TEXTURE2D(_TurfNormal);
            TEXTURE2D(_SandNormal);
            TEXTURE2D(_RockTex);
            TEXTURE2D(_RockNormal);
            TEXTURE2D(_BeachTex);
            TEXTURE2D(_BeachNormal);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 stripe : TEXCOORD0;
                float2 masksA : TEXCOORD1;
                float2 masksB : TEXCOORD2;
                float2 mow : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 weights : TEXCOORD2;
                float4 masks : TEXCOORD3;
                float4 stripe : TEXCOORD4; // xy stripe coordinates, zw mowing direction
                half fogFactor : TEXCOORD5;
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
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.weights = input.color;
                output.masks = float4(input.masksA, input.masksB);
                output.stripe = float4(input.stripe, input.mow);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half3 Layer(TEXTURE2D_PARAM(tex, samp), float2 uv, half4 tint)
            {
                half3 albedo = SAMPLE_TEXTURE2D(tex, samp, uv).rgb;
                return tint.rgb * lerp(half3(1, 1, 1), albedo * 1.6, _TextureStrength);
            }

            // Antialiased alternating band: -1 / +1 across bands of the given width.
            half Bands(float coord, float width)
            {
                float f = coord / width;
                float w = max(fwidth(f), 1e-4);
                float tri = abs(frac(f) - 0.5) * 2;
                return (half)(smoothstep(0.5 - w, 0.5 + w, tri) * 2 - 1);
            }

            half3 Whiteout(half3 tn, half3 n, half strength)
            {
                tn.xy *= strength;
                return normalize(half3(tn.x + n.x, abs(tn.z) * n.y, tn.y + n.z));
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = input.positionWS;
                half3 n = normalize(input.normalWS);
                float2 uv = p.xz * _Tiling;

                // Low-frequency patchiness so large lawns do not read as one flat colour.
                half macroA = SAMPLE_TEXTURE2D(_RoughTex, sampler_RoughTex, p.xz * 0.0071).g;
                half macroB = SAMPLE_TEXTURE2D(_RoughTex, sampler_RoughTex, p.xz * 0.0213 + 0.37).g;
                half macro = saturate((macroA * 0.6 + macroB * 0.4 - 0.35) * 2.2);
                half wobble = macroB - 0.5;

                float sdG, sdB, sdF, sdD;
                if (_UseMasks > 0.5)
                {
                    sdG = input.masks.x; sdB = input.masks.y; sdF = input.masks.z; sdD = input.masks.w;
                }
                else
                {
                    half4 w = saturate(input.weights);
                    // Green spans +-2 m so ground without green stays clear of the 1.2-1.45 m collar band.
                    sdF = (0.5 - w.r) * 2; sdG = (0.5 - w.g) * 4; sdB = (0.5 - w.b) * 2; sdD = (0.5 - w.a) * 3;
                }
                float aa = 0.02;
                half wDeep = smoothstep(0.9, -0.9, sdD + wobble * 1.6);
                half wFair = smoothstep(0.08 + aa, -0.08 - aa, sdF + wobble * 0.12);
                half wCollar = smoothstep(1.45, 1.2, sdG + wobble * 0.08);
                half wGreen = smoothstep(0.035, -0.035, sdG);
                half wBunker = smoothstep(0.03, -0.03, sdB);

                // Mowing stripes read light or dark depending on whether the grass is bent toward or away from the
                // viewer, so they swap as the camera turns; a little of the pattern stays from the side.
                float2 mow = input.stripe.zw;
                half3 V0 = GetWorldSpaceNormalizeViewDir(p);
                float2 vh = V0.xz / max(length(V0.xz), 1e-3);
                half facing = dot(mow, mow) > 0.25 ? dot(vh, mow) : 0;
                half viewGain = 0.35 + _StripeViewGain * abs(facing);
                half viewSign = facing < 0 ? -1 : 1;

                // Two scales of the rough texture break up visible tiling across the big lawns.
                half3 roughNear = Layer(TEXTURE2D_ARGS(_RoughTex, sampler_RoughTex), uv, _RoughTint);
                half3 roughFar = Layer(TEXTURE2D_ARGS(_RoughTex, sampler_RoughTex), uv * 0.31 + 0.17, _RoughTint);
                half3 rough = lerp(roughNear, roughFar, 0.35 + 0.3 * wobble);
                half dry = saturate((macroA - 0.55) * 4) * saturate((macroB - 0.35) * 3) * _DryStrength;
                rough = lerp(rough, rough * _DryTint.rgb * 2.0, dry);
                half3 deep = Layer(TEXTURE2D_ARGS(_DeepRoughTex, sampler_RoughTex), uv * 0.8, _DeepRoughTint);
                deep = lerp(deep, deep * _ScrubTint.rgb * 2.2, saturate(macroA * 1.8 - 0.6) * 0.5);
                deep = lerp(deep, deep * _DryTint.rgb * 2.0, dry * 0.8);
                half3 fairway = Layer(TEXTURE2D_ARGS(_FairwayTex, sampler_RoughTex), uv * 1.5, _FairwayTint);
                fairway *= 1 + Bands(input.stripe.y, _StripeWidth) * _StripeStrength * viewGain * viewSign;
                half3 firstCut = Layer(TEXTURE2D_ARGS(_FairwayTex, sampler_RoughTex), uv * 1.1, _FirstCutTint);
                half3 collar = Layer(TEXTURE2D_ARGS(_FairwayTex, sampler_RoughTex), uv * 2.0, _CollarTint);
                half3 green = Layer(TEXTURE2D_ARGS(_GreenTex, sampler_RoughTex), uv * 2.0, _GreenTint);
                green *= 1 + Bands(input.stripe.y, _GreenStripeWidth) * _GreenStripeStrength * viewGain * viewSign;
                green *= 1 + Bands(input.stripe.x, _GreenStripeWidth * 1.3) * _GreenStripeStrength * 0.35;
                half3 sand = Layer(TEXTURE2D_ARGS(_BunkerTex, sampler_RoughTex), uv * 1.2, _BunkerTint);
                // Rake furrows along the line of play, fading out before they alias.
                float rake = (input.stripe.y + sin(input.stripe.x * 0.45) * 0.35) * 40.0;
                half rakeFade = saturate(1 - fwidth(rake) * 0.6);
                sand *= 1 + sin(rake) * _RakeStrength * rakeFade;

                half wFirst = smoothstep(_FirstCutWidth + 0.15, _FirstCutWidth - 0.15, sdF + wobble * 0.2) * (1 - wFair);
                half3 albedo = rough * lerp(1 - _MacroStrength * 0.5, 1 + _MacroStrength * 0.35, macro);
                albedo = lerp(albedo, deep, wDeep);
                albedo = lerp(albedo, firstCut * lerp(0.95, 1.03, macro), wFirst * (1 - wDeep));
                albedo = lerp(albedo, fairway * lerp(0.94, 1.04, macro), wFair);
                albedo = lerp(albedo, collar, wCollar * (1 - wGreen));
                albedo = lerp(albedo, green, wGreen);
                // Bunker lips: the grass rolling over the edge is longer and shaded; the sand under the lip is damp.
                half lipGrass = smoothstep(0.9, 0.0, sdB) * (1 - wBunker);
                albedo *= 1 - 0.28 * lipGrass;
                sand *= 1 - 0.32 * smoothstep(-0.8, 0.0, sdB);
                albedo = lerp(albedo, sand, wBunker);
                half smoothness = lerp(lerp(0.1, 0.22, wFair), 0.3, wGreen) * (1 - wBunker) + 0.12 * wBunker;

                half3 tnTurf = UnpackNormal(SAMPLE_TEXTURE2D(_TurfNormal, sampler_RoughTex, uv * 1.7));
                half3 tnSand = UnpackNormal(SAMPLE_TEXTURE2D(_SandNormal, sampler_RoughTex, uv * 1.2));
                half3 normalWS = Whiteout(lerp(tnTurf, tnSand, wBunker), n, _NormalStrength * lerp(1.0, 0.45, wGreen));

                // Coast: beach sand below the beach line, darker and glossy where wet, cliff rock on steep faces.
                float y = p.y + wobble * 0.6;
                half wBeach = smoothstep(_BeachTop + 0.5, _BeachTop - 0.4, y);
                half wWet = smoothstep(_SeaLevel + 0.7, _SeaLevel + 0.15, y);
                half wRock = smoothstep(_RockSlope + 0.07, _RockSlope - 0.07, n.y + wobble * 0.1);
                half3 beach = Layer(TEXTURE2D_ARGS(_BeachTex, sampler_RoughTex), p.xz * 0.21, _BeachTint);
                beach = lerp(beach, beach * _WetSandTint.rgb * 1.5, wWet);
                half3 tnBeach = UnpackNormal(SAMPLE_TEXTURE2D(_BeachNormal, sampler_RoughTex, p.xz * 0.21));
                albedo = lerp(albedo, beach, wBeach);
                smoothness = lerp(smoothness, lerp(0.12, 0.55, wWet), wBeach);
                normalWS = normalize(lerp(normalWS, Whiteout(tnBeach, n, 0.8), wBeach));

                half3 blend = pow(abs(n), 4);
                blend /= dot(blend, 1);
                float3 rp = p * _RockScale;
                half3 rockX = SAMPLE_TEXTURE2D(_RockTex, sampler_RoughTex, rp.zy).rgb;
                half3 rockY = SAMPLE_TEXTURE2D(_RockTex, sampler_RoughTex, rp.xz).rgb;
                half3 rockZ = SAMPLE_TEXTURE2D(_RockTex, sampler_RoughTex, rp.xy).rgb;
                half3 rock = rockX * blend.x + rockY * blend.y + rockZ * blend.z;
                // The cliff scan is ochre sandstone; the cove's bluffs read as weathered grey-tan under the low warm sun.
                rock = lerp(dot(rock, half3(0.3, 0.59, 0.11)).xxx, rock, 0.6) * _RockTint.rgb * 1.7;
                half3 tnX = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RoughTex, rp.zy));
                half3 tnY = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RoughTex, rp.xz));
                half3 tnZ = UnpackNormal(SAMPLE_TEXTURE2D(_RockNormal, sampler_RoughTex, rp.xy));
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                half3 rockN = normalize(tnX.zyx * blend.x + tnY.xzy * blend.y + tnZ.xyz * blend.z);
                albedo = lerp(albedo, rock, wRock);
                smoothness = lerp(smoothness, 0.16, wRock);
                normalWS = normalize(lerp(normalWS, rockN, wRock));

                half under = smoothstep(_SeaLevel, _SeaLevel - 1.5, p.y);
                albedo *= 1 - 0.35 * under;

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(p);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(p, 1), input.fogFactor);
                inputData.bakedGI = CourseBakedGI(p, normalWS, inputData.viewDirectionWS, input.positionCS.xy);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = saturate(albedo);
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                // Low sun behind the grass: blades glow at grazing angles (turf only, not sand, rock or beach).
                Light sun = GetMainLight(inputData.shadowCoord, p, inputData.shadowMask);
                half turf = (1 - wBunker) * (1 - wRock) * (1 - wBeach);
                half backlit = pow(saturate(dot(-inputData.viewDirectionWS, sun.direction)), 3) * pow(1 - saturate(dot(n, inputData.viewDirectionWS)), 2);
                color.rgb += albedo * sun.color * backlit * sun.shadowAttenuation * turf * _GrassSheen * lerp(1, 0.6, wGreen);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

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
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }

        // Probe-volume bake albedo. UV1 carries surface masks rather than lightmap UVs, so the ground is probe-lit
        // (receive GI = light probes) and bounces one average turf colour instead of a rasterised albedo.
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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UnityMetaInput meta = (UnityMetaInput)0;
                meta.Albedo = lerp(_RoughTint.rgb, _FairwayTint.rgb, 0.3) * 0.85;
                return UnityMetaFragment(meta);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
