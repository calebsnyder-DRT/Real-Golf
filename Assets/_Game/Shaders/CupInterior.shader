// The cup, drawn on a flat disc (a quad scaled to the cup diameter, centred on the hole) without cutting the ground:
// each pixel casts its view ray into a cylinder of the quad's radius and _Depth and shades the wall or the bottom it
// hits - white-painted liner at the top, darker plastic below, soil at the bottom - with the rim's own sun shadow.
Shader "Puttshack/CupInterior"
{
    Properties
    {
        _PaintColor ("Painted Rim", Color) = (0.92, 0.92, 0.9, 1)
        _LinerColor ("Liner", Color) = (0.2, 0.21, 0.2, 1)
        _BottomColor ("Bottom", Color) = (0.07, 0.06, 0.05, 1)
        _Depth ("Depth (m)", Float) = 0.102
        _PaintDepth ("Painted Band (m)", Float) = 0.025
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _PaintColor;
            half4 _LinerColor;
            half4 _BottomColor;
            float _Depth;
            float _PaintDepth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 centre : TEXCOORD1; // xyz centre, w radius
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                float r = length(TransformObjectToWorld(float3(0.5, 0, 0)) - c);
                o.centre = float4(c, r);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 c = input.centre.xyz;
                float R = input.centre.w;
                float3 p = input.positionWS;
                float2 o = p.xz - c.xz;
                float r2 = dot(o, o);
                clip(R * R - r2);

                float3 d = normalize(p - GetCameraPositionWS());
                d.y = min(d.y, -1e-3);
                float2 dxz = d.xz;
                float a = max(dot(dxz, dxz), 1e-6);
                float b = 2 * dot(o, dxz);
                float cc = r2 - R * R;
                float t = (-b + sqrt(max(b * b - 4 * a * cc, 0))) / (2 * a);
                float depth = -d.y * t;

                Light light = GetMainLight(TransformWorldToShadowCoord(p));
                float3 L = light.direction;
                half3 albedo;
                half lit;
                half3 nrm;
                if (depth < _Depth)
                {
                    float2 w = o + dxz * t;
                    float2 nW = -w / R;
                    half paint = step(depth, _PaintDepth);
                    albedo = lerp(_LinerColor.rgb, _PaintColor.rgb, paint);
                    // The wall facing the sun is lit down to where the far rim's shadow reaches.
                    float2 lxz = L.xz;
                    float ll = length(lxz);
                    float chord = 2 * R * saturate(dot(nW, lxz / max(ll, 1e-4)));
                    float reach = depth * ll / max(L.y, 0.05);
                    lit = step(reach, chord) * saturate(dot(nW, lxz / max(ll, 1e-4)));
                    nrm = float3(nW.x, 0, nW.y);
                }
                else
                {
                    float tb = _Depth / -d.y;
                    float2 q = o + dxz * tb;
                    depth = _Depth;
                    albedo = _BottomColor.rgb;
                    float reach = _Depth * length(L.xz) / max(L.y, 0.05);
                    float2 back = q + normalize(L.xz + 1e-5) * reach;
                    lit = step(dot(back, back), R * R) * saturate(L.y);
                    nrm = float3(0, 1, 0);
                }
                half ao = exp(-depth * 22);
                half3 ambient = SampleSH(nrm) * ao;
                half3 color = albedo * (ambient + light.color * lit * light.shadowAttenuation * 0.85);
                color = MixFog(color, InitializeInputDataFog(float4(p, 1), input.fogFactor));
                return half4(color, 1);
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
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 l : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.l = input.positionOS.xy;
                return o;
            }
            half frag(Varyings input) : SV_Target { clip(0.25 - dot(input.l, input.l)); return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
