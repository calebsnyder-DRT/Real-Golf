Shader "Puttshack/Character/Hair"
{
    // Hair, brow and lash cards. The core is alpha-tested and writes depth; a second blended pass draws the soft strand
    // tips (alpha between _FringeCutoff and _Cutoff) on top, so edges stay fine without MSAA or sorting every card.
    // Two Kajiya-Kay highlights: a white primary shifted toward the tip and a tinted secondary toward the root.
    Properties
    {
        [MainTexture] _BaseMap("Albedo (A = coverage)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        _Cutoff("Core Cutoff", Range(0, 1)) = 0.5
        _FringeCutoff("Fringe Cutoff", Range(0, 1)) = 0.06
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        _SpecColorA("Primary Highlight", Color) = (0.32, 0.3, 0.28, 1)
        _SpecColorB("Secondary Highlight", Color) = (0.9, 0.75, 0.6, 1)
        _ShiftA("Primary Shift", Range(-1, 1)) = 0.12
        _ShiftB("Secondary Shift", Range(-1, 1)) = -0.18
        _ExponentA("Primary Exponent", Range(1, 600)) = 220
        _ExponentB("Secondary Exponent", Range(1, 600)) = 40
        [Toggle] _StrandAlongV("Strands Run Along V", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }

    HLSLINCLUDE
    #define _MODE_HAIR 1
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Transparent-40" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HairCore"
            Tags { "LightMode" = "UniversalForward" }
            Cull[_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PPCharacterVertex
            #pragma fragment HairCoreFragment
            #include_with_pragmas "PPCharacterHairPragmas.hlsl"
            #include "PPCharacterInput.hlsl"
            #include "PPCharacterForward.hlsl"
            #include "PPCharacterHair.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "HairFringe"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull[_Cull]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PPCharacterVertex
            #pragma fragment HairFringeFragment
            #include_with_pragmas "PPCharacterHairPragmas.hlsl"
            #include "PPCharacterInput.hlsl"
            #include "PPCharacterForward.hlsl"
            #include "PPCharacterHair.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define _ALPHATEST_ON 1
            #include "PPCharacterInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #define _ALPHATEST_ON 1
            #include "PPCharacterInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #define _ALPHATEST_ON 1
            #include "PPCharacterInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
