#ifndef PP_CHARACTER_INPUT_INCLUDED
#define PP_CHARACTER_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// One layout for every character shader so the SRP Batcher can batch skin, cloth, eye and hair draws.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    half _BumpScale;
    half _Smoothness;
    half _Metallic;
    half _OcclusionStrength;
    half _CavityStrength;
    half _DetailNormalScale;
    float _DetailTiling;
    half4 _ScatterColor;
    half _Wrap;
    half _Transmission;
    half4 _TransmissionColor;
    half _SecondLobeSmoothness;
    half _SecondLobeMix;
    half4 _SheenColor;
    half _SheenRoughness;
    half _Catchlight;
    half4 _SpecColorA;
    half4 _SpecColorB;
    half _ShiftA;
    half _ShiftB;
    half _ExponentA;
    half _ExponentB;
    half _StrandAlongV;
    half _FringeCutoff;
    half _Surface;
CBUFFER_END

// _BaseMap, _BumpMap and the Alpha()/SampleAlbedoAlpha() helpers the URP shadow and depth passes call.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

TEXTURE2D(_MaskMap);            SAMPLER(sampler_MaskMap);
TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);

#endif
