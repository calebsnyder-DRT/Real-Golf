// Equirectangular HDR sky with a sea-haze band: near the horizon the sky blends into the fog colour (warmer toward
// the sun) and below the horizon it is pure haze, so distant ocean and coast dissolve into the sky like coastal air.
// Azimuth convention: direction (sin a, 0, cos a) samples u = 0.5 + (a - _Rotation) / 360.
Shader "Puttshack/HazySky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Sky (HDR, equirectangular)", 2D) = "grey" {}
        _Exposure ("Exposure", Range(0, 8)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Rotation ("Rotation (deg)", Range(-360, 360)) = 0
        _HazeColor ("Haze Colour", Color) = (0.86, 0.84, 0.82, 1)
        _HazeSunColor ("Haze Colour Toward Sun", Color) = (1.0, 0.82, 0.62, 1)
        _HazeHeight ("Haze Height (sin elevation)", Range(0.001, 0.5)) = 0.08
        _HazeStrength ("Haze Strength", Range(0, 1)) = 0.85
        _SunDirection ("Sun Direction (world, toward sun)", Vector) = (0, 0.1, 1, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                half4 _Tint;
                float _Rotation;
                half4 _HazeColor;
                half4 _HazeSunColor;
                float _HazeHeight;
                half _HazeStrength;
                float4 _SunDirection;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float azimuth = atan2(d.x, d.z) - radians(_Rotation);
                float u = frac(0.5 + azimuth / (2 * PI));
                float elevation = asin(clamp(d.y, -1, 1));
                float v = 0.5 + max(elevation, 0.002) / PI;
                half3 sky = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, float2(u, v), 0).rgb * _Tint.rgb * _Exposure;

                float3 sun = normalize(_SunDirection.xyz);
                half toward = saturate(dot(normalize(float3(d.x, 0, d.z)), normalize(float3(sun.x, 0, sun.z))) * 0.5 + 0.5);
                half3 haze = lerp(_HazeColor.rgb, _HazeSunColor.rgb, pow(toward, 4));
                half band = pow(saturate(1 - max(d.y, 0) / _HazeHeight), 2.2) * _HazeStrength;
                half3 color = lerp(sky, haze, d.y <= 0 ? 1 : band);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
