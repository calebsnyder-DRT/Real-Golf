// GPU-instanced grass for the rough, first cut, deep rough and fescue (Puttshack.Runtime.Course.GrassField). Each
// instance is a clump of blades read from _GrassInstances (Graphics.RenderMeshPrimitives, one draw per 24 m tile).
// The clump mesh carries blade roots in unit-radius clump space: POSITION = (root x, t along blade, root z),
// NORMAL = blade facing, TEXCOORD0 = (side * taper, t), TEXCOORD1 = (blade random, lean angle).
// Instances within a tile are shuffled, so the distance thinning keeps a constant on-screen density: instance rank r
// survives out to _DensityStart / sqrt(r), shrinking away just before, while the survivors widen to hold coverage.
Shader "Puttshack/GrassInstanced"
{
    Properties
    {
        _FirstCutBase ("First Cut Base", Color) = (0.16, 0.29, 0.08, 1)
        _FirstCutTip ("First Cut Tip", Color) = (0.36, 0.53, 0.19, 1)
        _RoughBase ("Rough Base", Color) = (0.13, 0.24, 0.07, 1)
        _RoughTip ("Rough Tip", Color) = (0.34, 0.47, 0.17, 1)
        _DeepBase ("Deep Rough Base", Color) = (0.12, 0.2, 0.06, 1)
        _DeepTip ("Deep Rough Tip", Color) = (0.4, 0.44, 0.18, 1)
        _FescueBase ("Fescue Base", Color) = (0.2, 0.24, 0.09, 1)
        _FescueTip ("Fescue Tip", Color) = (0.68, 0.6, 0.34, 1)
        _DryColor ("Dry Patch Tint", Color) = (0.62, 0.55, 0.3, 1)
        _DryAmount ("Dry Patches", Range(0, 1)) = 0.35
        _HeightMin ("Height Min (cut, rough, deep, fescue)", Vector) = (0.035, 0.06, 0.13, 0.3)
        _HeightMax ("Height Max (cut, rough, deep, fescue)", Vector) = (0.055, 0.11, 0.24, 0.6)
        _BladeWidth ("Blade Width (m)", Vector) = (0.006, 0.008, 0.009, 0.006)
        _ClumpRadius ("Clump Radius (m)", Vector) = (0.09, 0.12, 0.14, 0.16)
        _Bend ("Blade Bend", Vector) = (0.15, 0.25, 0.35, 0.55)
        _RootOcclusion ("Root Occlusion", Range(0, 1)) = 0.6
        _GroundNormalBlend ("Ground Normal Blend", Range(0, 1)) = 0.55
        _Wrap ("Light Wrap", Range(0, 1)) = 0.5
        _Translucency ("Translucency", Range(0, 3)) = 1.1
        _Sheen ("Sheen", Range(0, 0.5)) = 0.08
        _DensityStart ("Full Density Distance (m)", Float) = 14
        _FadeEnd ("Fade End Distance (m)", Float) = 70
        _MaxWiden ("Max Blade Widening", Range(1, 4)) = 2.4
        _WindDirection ("Wind Direction (xz)", Vector) = (1, 0, 0.35, 0)
        _WindStrength ("Wind Strength", Range(0, 1)) = 0.3
        _WindSpeed ("Wind Speed", Float) = 1.4
        _WindWaveLength ("Gust Wave Length (m)", Float) = 9
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct GrassInstance
        {
            float3 position;
            uint packed; // yaw 8 | scale 6 | type 2 | ground normal x 6 | ground normal z 6 | random 4
        };

        StructuredBuffer<GrassInstance> _GrassInstances;
        float _GrassInstanceOffset;
        float _GrassInstanceCount;
        float4 _GrassInteractor; // xyz ball position, w flatten radius (global, GrassField.SetBallPosition)

        CBUFFER_START(UnityPerMaterial)
            half4 _FirstCutBase;
            half4 _FirstCutTip;
            half4 _RoughBase;
            half4 _RoughTip;
            half4 _DeepBase;
            half4 _DeepTip;
            half4 _FescueBase;
            half4 _FescueTip;
            half4 _DryColor;
            half _DryAmount;
            float4 _HeightMin;
            float4 _HeightMax;
            float4 _BladeWidth;
            float4 _ClumpRadius;
            float4 _Bend;
            half _RootOcclusion;
            half _GroundNormalBlend;
            half _Wrap;
            half _Translucency;
            half _Sheen;
            float _DensityStart;
            float _FadeEnd;
            float _MaxWiden;
            float4 _WindDirection;
            float _WindStrength;
            float _WindSpeed;
            float _WindWaveLength;
        CBUFFER_END

        struct GrassAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv0 : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
        };

        struct GrassVertex
        {
            float3 positionWS;
            half3 normalWS;
            half3 groundNormalWS;
            half3 albedo;
            half t;
            half tipGlow;
        };

        float GrassHash(float2 p)
        {
            p = frac(p * float2(0.1031, 0.1030));
            p += dot(p, p.yx + 33.33);
            return frac((p.x + p.y) * p.x);
        }

        float GrassValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3 - 2 * f);
            float a = GrassHash(i), b = GrassHash(i + float2(1, 0));
            float c = GrassHash(i + float2(0, 1)), d = GrassHash(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        GrassVertex BuildGrassVertex(GrassAttributes input, uint instanceID)
        {
            GrassInstance inst = _GrassInstances[(uint)(_GrassInstanceOffset + 0.5) + instanceID];
            uint packed = inst.packed;
            float yaw = (packed & 0xFF) * (TWO_PI / 256.0);
            float scale = ((packed >> 8) & 0x3F) / 63.0;
            uint type = (packed >> 14) & 0x3;
            float gx = ((packed >> 16) & 0x3F) / 31.5 - 1;
            float gz = ((packed >> 22) & 0x3F) / 31.5 - 1;
            float rnd = ((packed >> 28) & 0xF) / 15.0;
            float4 mask = float4(type == 0, type == 1, type == 2, type == 3);

            float3 groundN = normalize(float3(gx, sqrt(saturate(1 - gx * gx - gz * gz)) + 0.05, gz));
            float3 root0 = inst.position;
            float t = input.uv0.y;
            float bladeRnd = input.uv1.x;

            // Distance thinning by rank within the tile (see header), and the blade widening that keeps coverage.
            float dist = distance(_WorldSpaceCameraPos.xyz, root0);
            float rank = ((float)instanceID + 0.5) / max(_GrassInstanceCount, 1);
            float reach = min(_DensityStart * rsqrt(rank), _FadeEnd);
            float life = saturate((reach - dist) / (reach * 0.18));
            float density = saturate(_DensityStart * _DensityStart / max(dist * dist, 1e-3));
            float widen = clamp(rsqrt(max(density, 1e-3)), 1, _MaxWiden);

            float height = lerp(dot(_HeightMin, mask), dot(_HeightMax, mask), scale) * lerp(0.7, 1.15, bladeRnd) * life;
            float width = dot(_BladeWidth, mask) * widen * lerp(0.75, 1.2, frac(bladeRnd * 7.31));
            float radius = dot(_ClumpRadius, mask) * lerp(0.75, 1.25, rnd);
            float bend = dot(_Bend, mask) * lerp(0.6, 1.3, frac(bladeRnd * 3.7));

            float s, c;
            sincos(yaw, s, c);
            float2x2 rot = float2x2(c, -s, s, c);
            float2 rootXZ = mul(rot, input.positionOS.xz) * radius;
            float3 root = root0 + float3(rootXZ.x, 0, rootXZ.y) - groundN * 0.01;
            root.y -= dot(groundN.xz, rootXZ) / groundN.y;

            float2 facing2 = mul(rot, input.normalOS.xz);
            float lean = input.uv1.y + yaw;
            float3 leanDir = float3(cos(lean), 0, sin(lean));

            // Ball flattening: blades inside the interactor radius fold away from the ball.
            float3 toBlade = root - _GrassInteractor.xyz;
            float press = _GrassInteractor.w > 0 ? 1 - saturate(length(toBlade) / _GrassInteractor.w) : 0;
            press *= saturate(1 - abs(toBlade.y) * 4);
            float2 away = normalize(toBlade.xz + 1e-4);
            leanDir = normalize(lerp(leanDir, float3(away.x, 0, away.y), press));
            bend = lerp(bend, 1.4, press);
            height *= 1 - press * 0.45;

            // Wind: travelling gust stripes across the course plus a quick per-blade shiver.
            float2 wdir = normalize(_WindDirection.xz + 1e-5);
            float wave = dot(root.xz, wdir) * (TWO_PI / max(_WindWaveLength, 0.1)) - _Time.y * _WindSpeed;
            float gust = 0.5 + 0.5 * sin(wave) * (0.6 + 0.4 * sin(wave * 0.37 + 1.3));
            float shiver = sin(_Time.y * _WindSpeed * 4.7 + bladeRnd * 23.0 + dot(root.xz, float2(3.1, 2.3))) * 0.15;
            float windAmount = _WindStrength * (gust + shiver) * (1 - press) * lerp(0.6, 1.6, mask.w);

            float3 up = normalize(lerp(float3(0, 1, 0), groundN, 0.25));
            float3 windDir3 = float3(wdir.x, 0, wdir.y);
            float t2 = t * t;
            float3 spine = up * t * height + (leanDir * bend + windDir3 * windAmount) * t2 * height;
            spine.y -= (bend * bend * 0.35 + windAmount * windAmount * 0.5) * t2 * height;

            float3 facing = normalize(float3(facing2.x, 0, facing2.y));
            float3 across = normalize(cross(up, facing));
            float3 positionWS = root + spine + across * (input.uv0.x * width);

            float3 V = normalize(_WorldSpaceCameraPos.xyz - positionWS);
            facing *= dot(facing, V) < 0 ? -1 : 1;
            float3 bladeN = normalize(facing + across * input.uv0.x * 0.6 + leanDir * 0.25 + up * 0.3);

            half3 baseCol = _FirstCutBase.rgb * mask.x + _RoughBase.rgb * mask.y + _DeepBase.rgb * mask.z + _FescueBase.rgb * mask.w;
            half3 tipCol = _FirstCutTip.rgb * mask.x + _RoughTip.rgb * mask.y + _DeepTip.rgb * mask.z + _FescueTip.rgb * mask.w;
            half3 albedo = lerp(baseCol, tipCol, pow(t, 1.3));
            float patch = GrassValueNoise(root0.xz * 0.07) * 0.65 + GrassValueNoise(root0.xz * 0.23) * 0.35;
            float dry = saturate((patch - 0.55) * 3) * _DryAmount * (1 - mask.x * 0.7);
            albedo = lerp(albedo, albedo * _DryColor.rgb * 2.2, dry * t);
            albedo *= lerp(0.85, 1.12, rnd) * lerp(1 - _RootOcclusion, 1, saturate(t * 1.6));

            GrassVertex o;
            o.positionWS = positionWS;
            o.normalWS = bladeN;
            o.groundNormalWS = groundN;
            o.albedo = albedo;
            o.t = t;
            o.tipGlow = saturate(t * 1.4) * (0.6 + 0.4 * mask.w + 0.3 * mask.z);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../CourseGI.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 groundNormalWS : TEXCOORD2;
                half4 albedoT : TEXCOORD3;
                half2 glowFog : TEXCOORD4;
            };

            Varyings vert(GrassAttributes input, uint instanceID : SV_InstanceID)
            {
                GrassVertex g = BuildGrassVertex(input, instanceID);
                Varyings output;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.positionWS = g.positionWS;
                output.normalWS = g.normalWS;
                output.groundNormalWS = g.groundNormalWS;
                output.albedoT = half4(g.albedo, g.t);
                output.glowFog = half2(g.tipGlow, ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                half3 V = GetWorldSpaceNormalizeViewDir(p);
                half3 Ng = normalize(input.groundNormalWS);
                half3 N = normalize(lerp(normalize(input.normalWS), Ng, _GroundNormalBlend));
                half3 albedo = input.albedoT.rgb;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half ssaoIndirect = 1, ssaoDirect = 1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ssaoIndirect = ssao.indirectAmbientOcclusion;
                    ssaoDirect = ssao.directAmbientOcclusion;
                #endif

                Light light = GetMainLight(TransformWorldToShadowCoord(p), p, half4(1, 1, 1, 1));
                half shadow = light.shadowAttenuation * light.distanceAttenuation;
                half ndl = dot(N, light.direction);
                half wrap = saturate((ndl + _Wrap) / (1 + _Wrap));
                half3 direct = albedo * light.color * wrap * shadow * ssaoDirect;

                half back = pow(saturate(dot(-V, light.direction)), 3);
                half3 trans = albedo * 2.5 * light.color * back * _Translucency * input.glowFog.x * lerp(0.2, 1, shadow);

                half3 H = normalize(light.direction + V);
                half3 sheen = light.color * pow(saturate(dot(N, H)), 20) * _Sheen * shadow * input.albedoT.a;

                half3 ambient = CourseBakedGI(p, Ng, V, input.positionCS.xy) * albedo * ssaoIndirect;
                half3 color = ambient + direct + trans + sheen;
                color = MixFog(color, InitializeInputDataFog(float4(p, 1), input.glowFog.y));
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(GrassAttributes input, uint instanceID : SV_InstanceID) : SV_POSITION
            {
                return TransformWorldToHClip(BuildGrassVertex(input, instanceID).positionWS);
            }

            half frag(float4 positionCS : SV_POSITION) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings vert(GrassAttributes input, uint instanceID : SV_InstanceID)
            {
                GrassVertex g = BuildGrassVertex(input, instanceID);
                Varyings output;
                output.positionCS = TransformWorldToHClip(g.positionWS);
                output.normalWS = normalize(lerp(g.normalWS, g.groundNormalWS, _GroundNormalBlend));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
