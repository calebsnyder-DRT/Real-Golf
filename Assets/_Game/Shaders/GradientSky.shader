// Clear-day sky as an explicit gradient.
//
// This replaces a photographic HDRI whose horizon glow rendered at 0.56 luminance
// and blue-over-green 1.13, against the reference art's 0.385 and 1.32. A real
// captured sky carries the haze of the day it was shot; the art keeps its blue
// saturated all the way down to the ridgeline, and no exposure setting recovers
// that once it has been baked into the capture.
Shader "RealGolf/GradientSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.215, 0.515, 0.825, 1)
        _HorizonColor ("Horizon", Color) = (0.430, 0.690, 0.898, 1)
        _GroundColor ("Below Horizon", Color) = (0.355, 0.520, 0.585, 1)
        _Exponent ("Falloff", Range(0.2, 4)) = 1.35
        _HorizonSoftness ("Horizon Softness", Range(0.001, 0.2)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float _Exponent;
                float _HorizonSoftness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Unity draws the skybox on a unit mesh centred on the camera, so
                // object space doubles as the view direction.
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                float up = saturate(d.y);

                // Curved rather than linear: a straight ramp puts too much of the
                // pale horizon colour into the upper sky and flattens the blue.
                float t = pow(up, 1.0 / max(_Exponent, 0.0001));
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);

                // The far scenery covers everything below the horizon, but the band
                // right at zero needs to agree with it or a seam shows through gaps.
                half blend = smoothstep(-_HorizonSoftness, _HorizonSoftness, d.y);
                half3 c = lerp(_GroundColor.rgb, sky, blend);

                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
