using System.Collections.Generic;
using RealGolf.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGolf.Core
{
    /// <summary>
    /// Creates the project's materials in code. Keeping them procedural means the
    /// game has no binary material dependencies and stays seed-driven end to end.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new();
        private static Shader _litShader;
        private static Shader _unlitShader;

        private static readonly Color[] PlayerPalette =
        {
            new(0.94f, 0.94f, 0.94f),
            new(0.95f, 0.35f, 0.30f),
            new(0.30f, 0.60f, 0.95f),
            new(0.98f, 0.82f, 0.25f)
        };

        private static Shader LitShader
        {
            get
            {
                if (_litShader == null)
                {
                    _litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                }

                return _litShader;
            }
        }

        private static Shader UnlitShader
        {
            get
            {
                if (_unlitShader == null)
                {
                    _unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                                   ?? Shader.Find("Unlit/Color")
                                   ?? LitShader;
                }

                return _unlitShader;
            }
        }

        public static Material Solid(string key, Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            if (Cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            var material = new Material(LitShader) { name = key };
            SetColor(material, color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", metallic);

            Cache[key] = material;
            return material;
        }

        public static Material Transparent(string key, Color color, float smoothness = 0.9f)
        {
            if (Cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            var material = new Material(LitShader) { name = key };
            SetColor(material, color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;

            Cache[key] = material;
            return material;
        }

        /// <summary>
        /// Unlit and additive-friendly, for things that should read clearly against
        /// the sky rather than take scene lighting, such as the shot tracer.
        /// </summary>
        public static Material TransparentUnlit(string key, Color color)
        {
            if (Cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            var material = new Material(UnlitShader) { name = key };
            SetColor(material, color);

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            Cache[key] = material;
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private const string CourseTextures = "Models/Course/Textures/";

        /// <summary>
        /// Clear-day sky as an authored gradient, with the CC0 HDRI kept as the
        /// fallback. Returns null only if neither is available, so callers can drop
        /// back to the hand-set ambient colours.
        /// </summary>
        /// <remarks>
        /// The HDRI was the sky until its horizon was measured against the reference
        /// art: it rendered the band above the ridgeline at 0.56 luminance and
        /// blue-over-green 1.13, where the art holds 0.385 and a still-saturated
        /// 1.32. That pale band is haze baked into the photograph on the day it was
        /// shot, and exposure cannot separate it from the rest of the sky -- pulling
        /// the horizon down to the art took the zenith to near black with it. A
        /// gradient gives the two ends independently.
        /// </remarks>
        public static Material Skybox
        {
            get
            {
                const string key = "Skybox";
                if (Cache.TryGetValue(key, out Material cached) && cached != null)
                {
                    return cached;
                }

                Shader gradient = Shader.Find("RealGolf/GradientSky");
                if (gradient != null)
                {
                    var sky = new Material(gradient) { name = key };

                    // Read off the reference art and extrapolated to the poles: it
                    // only shows about 15 degrees of sky above its ridges, running
                    // 0.278 luminance at the top of frame to 0.385 at the ridgeline.
                    sky.SetColor("_ZenithColor", new Color(0.235f, 0.520f, 0.780f));
                    sky.SetColor("_HorizonColor", new Color(0.430f, 0.690f, 0.898f));

                    // Matches the fog and the far ridges, so the three meet cleanly
                    // wherever the terrain does not quite reach the horizon.
                    sky.SetColor("_GroundColor", new Color(0.355f, 0.520f, 0.585f));

                    // A linear ramp spreads the pale horizon colour too far up and
                    // costs the upper sky its blue.
                    sky.SetFloat("_Exponent", 1.6f);
                    sky.SetFloat("_HorizonSoftness", 0.025f);

                    Cache[key] = sky;
                    return sky;
                }

                var texture = Resources.Load<Texture>("Models/Course/Sky/sky_clear_blue");
                Shader shader = Shader.Find("Skybox/Panoramic");
                if (texture == null || shader == null)
                {
                    return null;
                }

                var material = new Material(shader) { name = key };
                material.SetTexture("_MainTex", texture);
                material.SetFloat("_Mapping", 1f);    // latitude/longitude layout
                material.SetFloat("_ImageType", 0f);  // full 360 degrees
                material.SetFloat("_Exposure", 1.15f);

                // Must stay at zero. GameBootstrap aims the directional light at the
                // sun burnt into this HDRI, and any rotation here slides the sky out
                // from under the shadows.
                material.SetFloat("_Rotation", 0f);

                Cache[key] = material;
                return material;
            }
        }

        /// <summary>
        /// Ground material driven by the seamless maps authored in Blender. The
        /// albedo carries smoothness in its alpha, and tiling is expressed in
        /// metres because the terrain mesh uses world-space UVs.
        /// </summary>
        private static Material Ground(string key, string albedoName, string normalName, float metresPerTile, Color fallback, float fallbackSmoothness = 0.15f, float smoothnessScale = 0.35f)
        {
            if (Cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            var albedo = Resources.Load<Texture2D>(CourseTextures + albedoName);
            if (albedo == null)
            {
                return Solid(key, fallback, fallbackSmoothness);
            }

            var material = new Material(LitShader) { name = key };
            SetColor(material, Color.white);
            material.SetTexture("_BaseMap", albedo);
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedo);
            }

            // Terrain UVs are world position * 0.1, so one UV unit spans ten metres.
            float tiling = 10f / metresPerTile;
            var scale = new Vector2(tiling, tiling);
            material.SetTextureScale("_BaseMap", scale);

            var normal = Resources.Load<Texture2D>(CourseTextures + normalName);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetTextureScale("_BumpMap", scale);
                material.SetFloat("_BumpScale", 0.8f);
                material.EnableKeyword("_NORMALMAP");
            }

            material.SetFloat("_Metallic", 0f);

            // URP multiplies the base map's alpha by _Smoothness, and its grazing
            // reflection is scaled by (smoothness + reflectivity). The maps were baked
            // at 0.23-0.31 smoothness, which is glass for turf: down a fairway the whole
            // surface picks up the sky at grazing angles and the green washes to grey.
            // Scaling it down here is what keeps the grass looking like grass.
            material.SetFloat("_Smoothness", smoothnessScale);
            material.SetFloat("_SmoothnessTextureChannel", 1f);
            material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            Cache[key] = material;
            return material;
        }

        public static Material ForSurface(SurfaceType surface) => surface switch
        {
            SurfaceType.Fairway => Ground("Fairway", "fairway_albedo", "turf_normal", 4f, new Color(0.36f, 0.62f, 0.26f)),
            SurfaceType.TeeBox => Ground("TeeBox", "fairway_albedo", "turf_normal", 3f, new Color(0.33f, 0.58f, 0.24f)),
            // Tighter mow pattern so the putting surface reads as its own thing from
            // the fairway approach rather than blending into it.
            SurfaceType.Green => Ground("Green", "green_albedo", "turf_normal", 2f, new Color(0.47f, 0.76f, 0.33f), 0.25f, 0.40f),
            // Collar: the green's own texture at a coarser mow, a shade duller, so the
            // fringe reads as a boundary without looking like a separate surface.
            SurfaceType.Collar => Ground("Collar", "green_albedo", "turf_normal", 3f, new Color(0.40f, 0.66f, 0.28f), 0.20f, 0.36f),
            SurfaceType.Rough => Ground("Rough", "rough_albedo", "rough_normal", 5f, new Color(0.26f, 0.46f, 0.19f)),
            SurfaceType.DeepRough => Ground("DeepRough", "deeprough_albedo", "rough_normal", 6f, new Color(0.19f, 0.34f, 0.14f)),
            // Dry sand is matte but not dead, and it is the brightest thing on the
            // course, so it keeps more of its baked smoothness than the turf does.
            SurfaceType.Bunker => Ground("Bunker", "bunker_albedo", "sand_normal", 2.5f, new Color(0.88f, 0.82f, 0.62f), 0.05f, 0.70f),
            SurfaceType.Water => Solid("WaterBed", new Color(0.24f, 0.22f, 0.16f)),
            _ => Solid("Default", Color.magenta)
        };

        /// <summary>
        /// Dark body colour on purpose: water reads as water because of what it
        /// reflects, and now that there is a real sky to reflect, a bright blue
        /// slab just looks like painted plastic next to the rebalanced turf.
        /// </summary>
        public static Material WaterSurface =>
            Transparent("Water", new Color(0.08f, 0.20f, 0.26f, 0.82f), 0.95f);
        // Kenney's tree kit ships flat untextured materials in a stylised palette --
        // its "leafsGreen" is actually turquoise -- which does not survive a
        // photoreal sunlit course. These are the replacements, authored in sRGB
        // against the ground albedos already in use (fairway 0.130, green 0.150,
        // rough 0.105, deep rough 0.075 linear luminance). A real canopy reads
        // darker than mown turf, but not as dark as a measured one: these kit trees
        // are flat untextured blobs with no self-shadowing inside the canopy to
        // supply the usual variation, so they are pitched a little high on purpose.
        // Broadleaf averages 0.083 (about 64% of the fairway) and conifers 0.044.
        //
        // Several variants exist purely so a treeline is not one flat wall of green.
        // They are separate materials rather than one material driven by a
        // MaterialPropertyBlock because an MPB opts the renderer out of the SRP
        // Batcher, and there can be 300 trees on a hole.
        private static readonly Color[] BroadleafGreens =
        {
            new Color(0.245f, 0.345f, 0.165f),
            new Color(0.215f, 0.315f, 0.150f),
            new Color(0.275f, 0.375f, 0.180f),
            new Color(0.228f, 0.330f, 0.192f),
            new Color(0.288f, 0.358f, 0.158f)
        };

        private static readonly Color[] ConiferGreens =
        {
            new Color(0.165f, 0.255f, 0.178f),
            new Color(0.142f, 0.228f, 0.160f),
            new Color(0.188f, 0.278f, 0.185f),
            new Color(0.155f, 0.245f, 0.198f)
        };

        private static readonly Color[] BroadleafBarks =
        {
            new Color(0.300f, 0.222f, 0.155f),
            new Color(0.265f, 0.200f, 0.145f),
            new Color(0.335f, 0.252f, 0.178f)
        };

        private static readonly Color[] ConiferBarks =
        {
            new Color(0.238f, 0.176f, 0.128f),
            new Color(0.212f, 0.158f, 0.118f)
        };

        public static int FoliageVariants(bool conifer) =>
            (conifer ? ConiferGreens : BroadleafGreens).Length;

        public static int BarkVariants(bool conifer) =>
            (conifer ? ConiferBarks : BroadleafBarks).Length;

        public static Material Foliage(int variant, bool conifer)
        {
            Color[] palette = conifer ? ConiferGreens : BroadleafGreens;
            int i = Wrap(variant, palette.Length);
            return Solid($"Foliage{(conifer ? "C" : "B")}{i}", palette[i], 0.06f);
        }

        public static Material Bark(int variant, bool conifer)
        {
            Color[] palette = conifer ? ConiferBarks : BroadleafBarks;
            int i = Wrap(variant, palette.Length);
            return Solid($"Bark{(conifer ? "C" : "B")}{i}", palette[i], 0.05f);
        }

        private static int Wrap(int value, int length) => ((value % length) + length) % length;

        public static Material TreeTrunk => Bark(0, false);
        public static Material TreeFoliage => Foliage(0, false);
        public static Material BallWhite => Solid("Ball", new Color(0.97f, 0.97f, 0.95f), 0.55f);
        public static Material FlagRed => Solid("Flag", new Color(0.85f, 0.16f, 0.16f), 0.1f);
        public static Material PoleWhite => Solid("Pole", new Color(0.92f, 0.92f, 0.92f), 0.4f);
        public static Material CupBlack => Solid("Cup", new Color(0.04f, 0.04f, 0.04f), 0f);
        public static Material TeeMarker => Solid("TeeMarker", new Color(0.90f, 0.75f, 0.15f), 0.3f);
        public static Material AimMarker => Solid("AimMarker", new Color(1f, 0.95f, 0.35f), 0.2f);

        public static Material PlayerColor(int index) =>
            Solid($"Player{index}", PlayerPalette[index % PlayerPalette.Length], 0.55f);

        /// <summary>Flight tracer tinted to match the player's ball.</summary>
        public static Material Tracer(int index)
        {
            Color color = PlayerPalette[index % PlayerPalette.Length];
            color.a = 0.7f;
            return TransparentUnlit($"Tracer{index}", color);
        }

        /// <summary>Downhill arrow drawn on the green while a putt is being lined up.</summary>
        public static Material SlopeArrow => TransparentUnlit("SlopeArrow", new Color(0.25f, 0.85f, 1f, 0.8f));

        /// <summary>
        /// Material for the flagstick baked in Blender. The whole prop shares one
        /// atlas, and its smoothness rides in the base map's alpha channel.
        /// </summary>
        public static Material Flagstick
        {
            get
            {
                const string key = "FlagstickBaked";
                if (Cache.TryGetValue(key, out Material cached) && cached != null)
                {
                    return cached;
                }

                var albedo = Resources.Load<Texture2D>("Models/Golf/Textures/flagstick_albedo");
                if (albedo == null)
                {
                    return PoleWhite;
                }

                var material = new Material(LitShader) { name = key };
                SetColor(material, Color.white);
                material.SetTexture("_BaseMap", albedo);
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", albedo);
                }

                var normal = Resources.Load<Texture2D>("Models/Golf/Textures/flagstick_normal");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 1f);
                    material.EnableKeyword("_NORMALMAP");
                }

                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 1f);
                material.SetFloat("_SmoothnessTextureChannel", 1f);
                material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

                Cache[key] = material;
                return material;
            }
        }
    }
}
