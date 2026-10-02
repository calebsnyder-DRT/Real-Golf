#ifndef PP_CHARACTER_HAIR_INCLUDED
#define PP_CHARACTER_HAIR_INCLUDED

// Per-strand jitter of the highlight shift, constant along a strand so highlights break into strands, not noise.
half PPStrandNoise(float2 uv)
{
    float across = _StrandAlongV > 0.5 ? uv.x : uv.y;
    return (half)(frac(sin(floor(across * 640.0) * 12.9898) * 43758.5453) - 0.5) * 0.15h;
}

half3 PPHairColor(Varyings input, PPSurface s)
{
    InputData inputData;
    PPInputData(input, s.normalTS, inputData);
    PPLightingInput p = PPLighting(inputData, s, input);
    p.strandNoise = PPStrandNoise(input.uv);
    half4 c = PPShade(inputData, s, p);
    return MixFog(c.rgb, inputData.fogCoord);
}

void HairCoreFragment(Varyings input, out half4 outColor : SV_Target0)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    PPSurface s = PPSampleSurface(input.uv);
    clip(s.alpha - _Cutoff);
    outColor = half4(PPHairColor(input, s), 1.0h);
}

half4 HairFringeFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    PPSurface s = PPSampleSurface(input.uv);
    clip(s.alpha - _FringeCutoff);
    clip(_Cutoff - s.alpha);
    half a = saturate((s.alpha - _FringeCutoff) / max(_Cutoff - _FringeCutoff, 1e-3h));
    return half4(PPHairColor(input, s), a);
}

#endif
