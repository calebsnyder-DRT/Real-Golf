// Distant scenery (mountain ridges, far treelines). Deliberately excluded from
// scene fog: exponential-squared fog at play-area density saturates to the fog
// colour long before 1.5km, which would erase the ridges into flat sky. The
// aerial perspective is instead baked into the vertical gradient, hazier at the
// base where there is more atmosphere between it and the eye.
Shader "RealGolf/BackdropUnlit"
{
    Properties
    {
        _HazeColor ("Haze Color (base)", Color) = (0.40, 0.59, 0.68, 1)
        _RidgeColor ("Ridge Color (top)", Color) = (0.33, 0.50, 0.57, 1)
        _GradientPower ("Gradient Power", Range(0.2, 4)) = 1.0
    }

    SubShader
    {
        // Ahead of Geometry so real terrain always wins, but still depth-writing
        // so the skybox pass does not paint over it.
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry-100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off // ridge rings are closed loops viewed from the inside

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _HazeColor;
                float4 _RidgeColor;
                float _GradientPower;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // uv.y runs 0 at the ridge base (deep haze) to 1 at the crest.
                half t = pow(saturate(IN.uv.y), _GradientPower);
                half3 c = lerp(_HazeColor.rgb, _RidgeColor.rgb, t);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
