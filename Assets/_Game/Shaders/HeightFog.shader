// Full-screen exponential height fog (Rendering.HeightFogFeature), before post-processing so it is tonemapped with
// the scene. Density falls off exponentially above a base height (sea level by default), so the cove and low ground
// gather marine haze while the clifftop stays clear; looking toward the sun the haze takes on forward-scattered
// sunlight. Sky pixels are fogged as if at a fixed far distance, scaled down because the HDR sky has its own haze.
// Globals come from CourseAtmosphere:
//   _PuttshackFogColor      rgb ambient haze
//   _PuttshackFogSunColor   rgb in-scattered sun, a = phase exponent
//   _PuttshackFogParams     x density at base (1/m), y height falloff (1/m), z base height (m), w start distance (m)
//   _PuttshackFogParams2    x max opacity, y sky amount, z sky distance (m)
//   _PuttshackFogSunDir     xyz direction toward the sun
Shader "Hidden/Puttshack/HeightFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "HeightFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _PuttshackFogColor;
            float4 _PuttshackFogSunColor;
            float4 _PuttshackFogParams;
            float4 _PuttshackFogParams2;
            float4 _PuttshackFogSunDir;

            // Optical depth of exp(-falloff * (h - base)) along origin + dir * t for t in [0, dist].
            float OpticalDepth(float3 origin, float3 dir, float dist)
            {
                float falloff = _PuttshackFogParams.y;
                float density = _PuttshackFogParams.x * exp(-falloff * (origin.y - _PuttshackFogParams.z));
                float k = falloff * dir.y * dist;
                float integral = abs(k) > 1e-3 ? (1 - exp(-k)) / k : 1 - 0.5 * k;
                return density * dist * integral;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 1e-6;
                #else
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                    bool sky = raw >= 1 - 1e-6;
                #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 ray = positionWS - _WorldSpaceCameraPos;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);
                if (sky) dist = _PuttshackFogParams2.z;

                float start = _PuttshackFogParams.w;
                float travelled = max(dist - start, 0);
                float3 origin = _WorldSpaceCameraPos + dir * min(start, dist);
                float fog = 1 - exp(-OpticalDepth(origin, dir, travelled));
                fog = min(fog, _PuttshackFogParams2.x) * (sky ? _PuttshackFogParams2.y : 1);

                float mu = saturate(dot(dir, _PuttshackFogSunDir.xyz));
                half3 inscatter = _PuttshackFogColor.rgb + _PuttshackFogSunColor.rgb * pow(mu, _PuttshackFogSunColor.a);
                color.rgb = lerp(color.rgb, inscatter, fog);
                return color;
            }
            ENDHLSL
        }
    }
}
