#ifndef PP_CHARACTER_FORWARD_INCLUDED
#define PP_CHARACTER_FORWARD_INCLUDED

// Forward pass for the golfer characters. Same inputs as URP Lit (probes, APV, Forward+ light loop, shadows, SSAO, fog)
// with the direct-light response swapped per mode:
//   _MODE_SKIN   per-channel wrap diffuse (red reaches furthest past the terminator), red-shifted shadow penumbra,
//                back-lit transmission through thin areas (mask B), two specular lobes, cavity-masked spec.
//   _MODE_CLOTH  soft wrap diffuse plus a Charlie sheen lobe at grazing angles.
//   _MODE_EYE    eyeball under the lids: mask R shades direct light too, faint wrap.
//   _MODE_HAIR   Kajiya-Kay strands with two shifted highlights (defined by PPCharacterHair.shader).
// Mask map: R ambient occlusion, G smoothness (scaled by _Smoothness), B thickness (0 = thin), A cavity.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight : TEXCOORD5;
#else
    half fogFactor : TEXCOORD5;
#endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct PPSurface
{
    half3 albedo;
    half alpha;
    half3 normalTS;
    half smoothness;
    half occlusion;
    half thickness;
    half cavity;
};

Varyings PPCharacterVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    half fogFactor = 0;
#if !defined(_FOG_FRAGMENT)
    fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.normalWS = normalInput.normalWS;
    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(vertexInput.positionWS, normalInput.normalWS));
#else
    output.fogFactor = fogFactor;
#endif
    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;
    return output;
}

PPSurface PPSampleSurface(float2 uv)
{
    PPSurface s;
    half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    s.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    s.alpha = albedoAlpha.a * _BaseColor.a;
    half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
    s.occlusion = lerp(1.0h, mask.r, _OcclusionStrength);
    s.smoothness = saturate(mask.g * _Smoothness);
    s.thickness = mask.b;
    s.cavity = lerp(1.0h, mask.a, _CavityStrength);
    half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
#if defined(_DETAIL_NORMAL)
    half3 d = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv * _DetailTiling), _DetailNormalScale);
    n = normalize(half3(n.xy + d.xy, n.z * d.z));
#endif
    s.normalTS = n;
    return s;
}

void PPInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
    inputData.tangentToWorld = tangentToWorld;
    inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
    inputData.viewDirectionWS = viewDirWS;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
#if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS,
        inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif
}

// ---------------------------------------------------------------- direct light

struct PPLightingInput
{
    half3 normalWS;
    half3 viewWS;
    half3 tangentWS;    // hair strand direction
    half thickness;
    half cavity;
    half occlusion;
    half strandNoise;
};

half PPStrand(half3 T, half3 V, half3 L, half exponent)
{
    half3 H = SafeNormalize(L + V);
    half th = dot(T, H);
    half sinTH = sqrt(saturate(1.0h - th * th));
    return smoothstep(-1.0h, 0.0h, th) * pow(sinTH, exponent);
}

half3 PPDirect(BRDFData brdf, BRDFData brdf2, Light light, PPLightingInput p)
{
    half3 N = p.normalWS;
    half3 V = p.viewWS;
    half3 L = light.direction;
    half NdotLRaw = dot(N, L);
    half NdotL = saturate(NdotLRaw);
    half shadow = light.shadowAttenuation;
    half dist = light.distanceAttenuation;
    half3 result = 0;

#if defined(_MODE_SKIN)
    // Per-channel wrap; the terminator is let through so the shadow map's self-shadow does not cut it hard.
    half3 w = _Wrap * _ScatterColor.rgb;
    half3 wrapped = saturate((NdotLRaw + w) / (1.0h + w));
    half leak = saturate(1.0h - (NdotLRaw * 4.0h + 0.5h)) * 0.8h;
    half s = lerp(shadow, 1.0h, leak);
    half3 shadow3 = pow(max(s, 1e-4h), 1.0h / (1.0h + _ScatterColor.rgb * 1.5h));
    result += brdf.diffuse * wrapped * shadow3;
    half3 Lt = L + N * 0.4h;
    half back = pow(saturate(dot(V, -Lt)), 4.0h) * _Transmission * (1.0h - p.thickness);
    result += brdf.diffuse * _TransmissionColor.rgb * back * lerp(shadow, 1.0h, 0.6h);
    half spec1 = DirectBRDFSpecular(brdf, N, L, V);
    half spec2 = DirectBRDFSpecular(brdf2, N, L, V);
    result += brdf.specular * lerp(spec1, spec2, _SecondLobeMix) * NdotL * shadow * p.cavity;
#elif defined(_MODE_CLOTH)
    half wrapped = saturate((NdotLRaw + _Wrap) / (1.0h + _Wrap));
    half s = lerp(shadow, 1.0h, saturate(1.0h - (NdotLRaw * 4.0h + 0.5h)) * 0.6h);
    result += brdf.diffuse * wrapped * s;
    result += brdf.specular * DirectBRDFSpecular(brdf, N, L, V) * NdotL * shadow;
    // Charlie sheen (Estevez & Kulla) with the Neubelt visibility; pi folded in like URP's own lobes.
    half3 H = SafeNormalize(L + V);
    half NoH = saturate(dot(N, H));
    half NoV = saturate(dot(N, V));
    half invR = 1.0h / max(_SheenRoughness, 0.05h);
    half sin2 = saturate(1.0h - NoH * NoH);
    half D = (2.0h + invR) * pow(max(sin2, 1e-4h), invR * 0.5h) * 0.5h;
    half Vis = 1.0h / max(4.0h * (NdotL + NoV - NdotL * NoV), 1e-3h);
    result += _SheenColor.rgb * D * Vis * NdotL * shadow;
#elif defined(_MODE_EYE)
    half wrapped = saturate((NdotLRaw + 0.25h) / 1.25h);
    result += brdf.diffuse * wrapped * shadow * p.occlusion;
    result += brdf.specular * DirectBRDFSpecular(brdf, N, L, V) * NdotL * shadow * p.occlusion;
#elif defined(_MODE_HAIR)
    half diffuse = saturate(lerp(0.3h, 1.0h, NdotLRaw * 0.5h + 0.5h)) * saturate(NdotLRaw + 0.6h);
    result += brdf.diffuse * diffuse * shadow;
    half3 T1 = normalize(p.tangentWS + N * (_ShiftA + p.strandNoise));
    half3 T2 = normalize(p.tangentWS + N * (_ShiftB + p.strandNoise));
    half3 spec = _SpecColorA.rgb * PPStrand(T1, V, L, _ExponentA)
               + _SpecColorB.rgb * brdf.albedo * PPStrand(T2, V, L, _ExponentB) * (0.6h + p.strandNoise * 2.0h);
    result += spec * saturate(NdotLRaw + 0.2h) * shadow * p.occlusion;
#else
    result += (brdf.diffuse + brdf.specular * DirectBRDFSpecular(brdf, N, L, V)) * NdotL * shadow;
#endif
    return result * light.color * dist;
}

half4 PPShade(InputData inputData, PPSurface s, PPLightingInput p)
{
    half alpha = s.alpha;
    BRDFData brdf;
    InitializeBRDFData(s.albedo, _Metallic, half3(0, 0, 0), s.smoothness, alpha, brdf);
    BRDFData brdf2 = brdf;
#if defined(_MODE_SKIN)
    half alpha2 = alpha;
    InitializeBRDFData(s.albedo, _Metallic, half3(0, 0, 0), lerp(s.smoothness, 1.0h, _SecondLobeSmoothness), alpha2, brdf2);
#endif
    const BRDFData noClearCoat = (BRDFData)0;

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, s.occlusion);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    half3 gi = GlobalIllumination(brdf, noClearCoat, 0.0, inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
        inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#if defined(_MODE_SKIN)
    // Ambient light scattered through the skin keeps occluded creases warm instead of grey.
    gi += brdf.diffuse * inputData.bakedGI * _ScatterColor.rgb * (1.0h - aoFactor.indirectAmbientOcclusion) * 0.35h;
#elif defined(_MODE_CLOTH)
    half NoV = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    gi += _SheenColor.rgb * inputData.bakedGI * pow(1.0h - NoV, 3.0h) * 0.5h * aoFactor.indirectAmbientOcclusion;
#endif

    half3 color = gi;
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        color += PPDirect(brdf, brdf2, mainLight, p);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            color += PPDirect(brdf, brdf2, light, p);
        }
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
    #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
    #endif
        {
            color += PPDirect(brdf, brdf2, light, p);
        }
    LIGHT_LOOP_END
#endif
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    color += inputData.vertexLighting * brdf.diffuse;
#endif
    return half4(color, alpha);
}

PPLightingInput PPLighting(InputData inputData, PPSurface s, Varyings input)
{
    PPLightingInput p;
    p.normalWS = inputData.normalWS;
    p.viewWS = inputData.viewDirectionWS;
    p.thickness = s.thickness;
    p.cavity = s.cavity;
    p.occlusion = s.occlusion;
    float3 bitangent = input.tangentWS.w * cross(input.normalWS.xyz, input.tangentWS.xyz);
    p.tangentWS = normalize(_StrandAlongV > 0.5h ? bitangent : input.tangentWS.xyz);
    p.strandNoise = 0;
    return p;
}

void PPCharacterFragment(Varyings input, out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    PPSurface s = PPSampleSurface(input.uv);
#if defined(_ALPHATEST_ON)
    clip(s.alpha - _Cutoff);
#endif
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    InputData inputData;
    PPInputData(input, s.normalTS, inputData);
    half4 color = PPShade(inputData, s, PPLighting(inputData, s, input));
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    outColor = half4(color.rgb, 1.0h);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
