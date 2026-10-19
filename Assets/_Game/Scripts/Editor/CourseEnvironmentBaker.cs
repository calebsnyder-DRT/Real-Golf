using System.IO;
using Puttshack.Content;
using Puttshack.Runtime;
using Puttshack.Runtime.Course;
using Puttshack.Runtime.Rendering;
using Puttshack.Runtime.Views;
using Puttshack.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Stillwater Cove surroundings and lighting for Main.unity, called from CourseBaker.BuildMainScene:
    /// - the Blender scenery (ArtSource/Blender/scripts/pp_pebble.py: surround terrain that continues the gameplay
    ///   ground exactly at its edge, the cove and ocean, rocks, cypress, the Lodge, pier, boats, forest) as a prefab
    ///   with the course ground and ocean materials;
    /// - the time of day from the active <see cref="CourseLightingPreset"/> (golden hour by default; midday, marine
    ///   layer and overcast alongside it in <see cref="PresetFolder"/>): sun with drifting cloud-shadow cookie and lens
    ///   flare (CourseSunRig gives the Pro Shop its own daylight), the hazy HDR sky turned so its sun glow sits behind
    ///   the light, skybox ambient, exponential sea haze plus full-screen height fog (CourseAtmosphere), reflection
    ///   probes over the course and each green;
    /// - a course-only post volume (ACES on HDR grading, bloom, warm balance, split toning, distance depth of field,
    ///   screen-space lens flare, film grain, vignette) that stops short of the shop, mode-driven depth of field on
    ///   the gameplay camera (CourseCameraFocus), SMAA;
    /// - the PC pipeline: GPU Resident Drawer with GPU occlusion culling, Adaptive Probe Volumes, 64-texel HDR LUT,
    ///   longer cascaded shadows, half-resolution SSAO and the height-fog renderer feature;
    /// - static flags on course renderers and a course probe volume, so `Puttshack/Bake Course GI` can bake sun and sky
    ///   bounce into the probe volume (see <see cref="BakeCourseGIMenu"/>).
    /// </summary>
    public static class CourseEnvironmentBaker
    {
        public const string Root = "Assets/_Game/Course/Baked/Environment";
        public const string SceneryModel = "Assets/_Game/Art/Course/PebbleBeach/PebbleBeach_Scenery.fbx";
        public const string SceneryPrefab = Root + "/PebbleBeach_Scenery.prefab";
        public const string TextureRoot = "Assets/_Game/Art/Course/PebbleBeach/Textures/";
        public const string ProfilePath = Root + "/Course_PostVolume.asset";
        public const string LightingSettingsPath = Root + "/Course_Lighting.lighting";
        public const string CloudCookiePath = Root + "/CloudShadows.png";
        public const string SunFlarePath = Root + "/SunFlare.asset";
        public const string HeightFogMaterialPath = Root + "/HeightFog.mat";
        public const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
        public const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        /// <summary>Authored time-of-day presets; created from code defaults when missing, then left to the artists.</summary>
        public const string PresetFolder = "Assets/_Game/Course/Lighting";
        public const string GoldenHourPreset = PresetFolder + "/GoldenHour.asset";
        public const string MiddayPreset = PresetFolder + "/Midday.asset";
        public const string MarineLayerPreset = PresetFolder + "/MarineLayer.asset";
        public const string OvercastPreset = PresetFolder + "/Overcast.asset";
        /// <summary>The preset Main.unity is baked with.</summary>
        public const string ActivePresetPath = GoldenHourPreset;

        /// <summary>Set to 1 to bake the course probe volume during BuildMainScene / BatchBuild (slow).</summary>
        public const string BakeGIEnvironmentVariable = "PUTTSHACK_BAKE_GI";

        /// <summary>Course post volume bounds: everything within ~1.7 km of the hole, but not the Pro Shop at x = 2500.</summary>
        public static readonly Vector3 VolumeCenter = new Vector3(-200f, 0f, 200f);
        public static readonly Vector3 VolumeSize = new Vector3(3200f, 1200f, 3400f);

        /// <summary>Margin of the course probe volume around the playable boundaries (m); the shop stays outside it.</summary>
        public const float ProbeVolumeMargin = 60f;

        // GPUResidentDrawerMode lives in Unity.RenderPipelines.GPUDriven.Runtime, which this assembly doesn't reference.
        private const int GpuResidentDrawerInstanced = 1;
        // BatchRendererGroup Variants "Keep All": the GPU Resident Drawer needs the DOTS instancing variants in builds.
        private const int BrgStrippingKeepAll = 2;

        public static Vector3 TowardSun => LoadActivePreset().TowardSun;

        public static void PlaceInScene(Scene scene)
        {
            EnsureFolder(Root);
            var root = new GameObject("Environment");
            SceneManager.MoveGameObjectToScene(root, scene);
            CourseLightingPreset preset = LoadActivePreset();

            Material ground = GroundMaterial();
            Material ocean = OceanMaterial();
            Material sky = SkyMaterial(preset);
            GameObject prefab = BuildSceneryPrefab(ground, ocean);
            var scenery = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            scenery.transform.SetParent(root.transform, false);

            CourseSunRig sun = ConfigureSun(preset);
            ConfigureRenderSettings(sky, preset);
            Volume volume = PlaceVolume(root.transform, preset);
            PlaceReflectionProbes(root.transform);
            PlaceProbeVolume(root.transform);
            PlaceAtmosphere(root.transform, preset, sun, volume);
            ConfigureCamera();
            ConfigurePipeline();
            BakeLighting(scene, System.Environment.GetEnvironmentVariable(BakeGIEnvironmentVariable) == "1");
        }

        // ---------------------------------------------------------------- presets

        public static CourseLightingPreset LoadActivePreset()
        {
            EnsurePresets();
            return AssetDatabase.LoadAssetAtPath<CourseLightingPreset>(ActivePresetPath);
        }

        /// <summary>Creates any missing time-of-day preset from its code defaults; existing (tuned) assets are kept.</summary>
        public static void EnsurePresets()
        {
            EnsureFolder(PresetFolder);
            var sky = AssetDatabase.LoadAssetAtPath<Texture>(TextureRoot + "pebble_sky.hdr");
            CreatePreset(GoldenHourPreset, sky, p => { });
            CreatePreset(MiddayPreset, sky, p =>
            {
                p.SunAzimuth = -150f;
                p.SunElevation = 58f;
                p.SunColor = new Color(1f, 0.95f, 0.88f);
                p.SunIntensity = 3.0f;
                p.ShadowStrength = 0.85f;
                p.FlareIntensity = 0.5f;
                p.SkyExposure = 1.3f;
                p.HazeColor = new Color(0.78f, 0.82f, 0.86f);
                p.HazeSunColor = new Color(1f, 0.96f, 0.9f);
                p.AmbientIntensity = 0.9f;
                p.FogColor = new Color(0.78f, 0.82f, 0.86f);
                p.FogDensity = 0.0006f;
                p.HeightFogColor = new Color(0.76f, 0.8f, 0.84f);
                p.HeightFogSunColor = new Color(0.5f, 0.46f, 0.4f);
                p.HeightFogDensity = 0.0007f;
                p.HeightFogSunExponent = 6f;
                p.CloudShadowStrength = 0.4f;
                p.PostExposure = 0.1f;
                p.Contrast = 14f;
                p.Saturation = 10f;
                p.Temperature = 3f;
                p.SplitShadows = new Color(0.5f, 0.54f, 0.6f);
                p.SplitHighlights = new Color(0.62f, 0.6f, 0.55f);
                p.SplitBalance = 0f;
                p.BloomIntensity = 0.3f;
                p.BloomTint = new Color(1f, 0.97f, 0.92f);
                p.VignetteIntensity = 0.18f;
            });
            CreatePreset(MarineLayerPreset, sky, p =>
            {
                p.SunAzimuth = -120f;
                p.SunElevation = 32f;
                p.SunColor = new Color(0.94f, 0.92f, 0.9f);
                p.SunIntensity = 1.7f;
                p.ShadowStrength = 0.5f;
                p.FlareIntensity = 0f;
                p.SkyExposure = 1.0f;
                p.HazeColor = new Color(0.74f, 0.77f, 0.8f);
                p.HazeSunColor = new Color(0.86f, 0.86f, 0.86f);
                p.HazeHeight = 0.2f;
                p.HazeStrength = 1f;
                p.AmbientIntensity = 1.05f;
                p.FogColor = new Color(0.72f, 0.75f, 0.78f);
                p.FogDensity = 0.0016f;
                p.HeightFogColor = new Color(0.72f, 0.75f, 0.78f);
                p.HeightFogSunColor = new Color(0.25f, 0.24f, 0.22f);
                p.HeightFogDensity = 0.004f;
                p.HeightFogFalloff = 0.045f;
                p.HeightFogStart = 10f;
                p.HeightFogMaxOpacity = 0.95f;
                p.HeightFogSkyAmount = 0.8f;
                p.CloudShadowStrength = 0.25f;
                p.CloudScale = 1100f;
                p.PostExposure = 0.35f;
                p.Contrast = 6f;
                p.Saturation = -6f;
                p.Temperature = -6f;
                p.SplitShadows = new Color(0.46f, 0.52f, 0.58f);
                p.SplitHighlights = new Color(0.6f, 0.6f, 0.58f);
                p.SplitBalance = 0f;
                p.BloomIntensity = 0.25f;
                p.BloomTint = Color.white;
                p.VignetteIntensity = 0.2f;
            });
            CreatePreset(OvercastPreset, sky, p =>
            {
                p.SunAzimuth = -135f;
                p.SunElevation = 45f;
                p.SunColor = new Color(0.9f, 0.92f, 0.95f);
                p.SunIntensity = 0.9f;
                p.ShadowStrength = 0.3f;
                p.FlareIntensity = 0f;
                p.SkyExposure = 0.85f;
                p.HazeColor = new Color(0.7f, 0.72f, 0.74f);
                p.HazeSunColor = new Color(0.78f, 0.79f, 0.8f);
                p.HazeHeight = 0.35f;
                p.HazeStrength = 1f;
                p.AmbientIntensity = 1.2f;
                p.FogColor = new Color(0.66f, 0.68f, 0.7f);
                p.FogDensity = 0.0012f;
                p.HeightFogColor = new Color(0.66f, 0.68f, 0.7f);
                p.HeightFogSunColor = Color.black;
                p.HeightFogDensity = 0.0018f;
                p.HeightFogFalloff = 0.025f;
                p.CloudShadowStrength = 0.15f;
                p.CloudScale = 1400f;
                p.PostExposure = 0.45f;
                p.Contrast = 8f;
                p.Saturation = -8f;
                p.Temperature = -3f;
                p.SplitShadows = new Color(0.5f, 0.54f, 0.58f);
                p.SplitHighlights = new Color(0.58f, 0.58f, 0.58f);
                p.SplitBalance = 0f;
                p.BloomIntensity = 0.2f;
                p.BloomTint = Color.white;
                p.VignetteIntensity = 0.16f;
            });
        }

        private static void CreatePreset(string path, Texture sky, System.Action<CourseLightingPreset> configure)
        {
            if (AssetDatabase.LoadAssetAtPath<CourseLightingPreset>(path) != null) return;
            var preset = ScriptableObject.CreateInstance<CourseLightingPreset>();
            preset.SkyTexture = sky;
            configure(preset);
            AssetDatabase.CreateAsset(preset, path);
        }

        // ---------------------------------------------------------------- materials

        private static Material GroundMaterial()
        {
            var hole = AssetDatabase.LoadAssetAtPath<Material>(CourseBaker.MaterialFolder + "/CourseGround.mat");
            Material m = hole != null ? new Material(hole) : CourseVisualAssets.CreateDefault().Ground;
            m.name = "CourseGroundScenery";
            m.SetFloat("_UseMasks", 0f);
            return Persist(m);
        }

        private static Material OceanMaterial()
        {
            var m = new Material(Shader.Find("Puttshack/Ocean")) { name = "Ocean" };
            m.SetTexture("_WaveNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "ocean_normal.png"));
            m.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "foam_noise.png"));
            return Persist(m);
        }

        private static Material SkyMaterial(CourseLightingPreset preset)
        {
            var m = new Material(Shader.Find("Puttshack/HazySky")) { name = "PebbleSky" };
            CourseAtmosphere.ApplySky(m, preset);
            return Persist(m);
        }

        private static Material Persist(Material generated)
        {
            string path = $"{Root}/{generated.name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            existing.shader = generated.shader;
            existing.CopyPropertiesFromMaterial(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---------------------------------------------------------------- scenery

        private static GameObject BuildSceneryPrefab(Material ground, Material ocean)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SceneryModel);
            if (model == null) throw new IOException($"Missing {SceneryModel}; export it from ArtSource/Blender/scripts/pp_pebble.py.");
            Material foliage = AssetDatabase.LoadAssetAtPath<Material>(CourseBaker.MaterialFolder + "/Foliage.mat");
            if (foliage == null) throw new IOException($"Missing {CourseBaker.MaterialFolder}/Foliage.mat; it is persisted with the hole prefabs.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "PebbleBeach_Scenery";
                foreach (MeshRenderer r in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        if (mats[i].name == "PB_Terrain") mats[i] = ground;
                        else if (mats[i].name == "PB_Ocean") mats[i] = ocean;
                        else if (mats[i].name.StartsWith(CourseVisualAssets.BlenderFoliagePrefix, System.StringComparison.Ordinal)) mats[i] = foliage;
                    }
                    r.sharedMaterials = mats;
                    bool sea = r.name == "PB_Ocean";
                    r.shadowCastingMode = sea ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                    r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                }
                MarkStatic(instance);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, SceneryPrefab, out bool ok);
                if (!ok || prefab == null) throw new IOException($"Could not save {SceneryPrefab}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Course renderers that never move: they contribute to the probe-volume bake (lit by probes, never lightmapped
        /// — the ground's UV1/UV2 carry surface masks), are captured by reflection probes, and swaying foliage writes
        /// per-object motion vectors. No batching-static flag: the GPU Resident Drawer batches them instead. The cup
        /// and flag are left dynamic.
        /// </summary>
        public static void MarkStatic(GameObject root)
        {
            Transform dynamicRoot = root.TryGetComponent(out BakedHole hole) ? hole.Cup : null;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (dynamicRoot != null && r.transform.IsChildOf(dynamicRoot)) continue;
                bool transparent = r.sharedMaterial != null && r.sharedMaterial.renderQueue >= (int)RenderQueue.Transparent;
                StaticEditorFlags flags = StaticEditorFlags.ReflectionProbeStatic;
                if (!transparent) flags |= StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                r.receiveGI = ReceiveGI.LightProbes;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                bool sways = false;
                foreach (Material m in r.sharedMaterials) sways |= m != null && m.shader != null && m.shader.name == "Puttshack/Foliage";
                if (sways) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            }
        }

        // ---------------------------------------------------------------- lighting

        private static CourseSunRig ConfigureSun(CourseLightingPreset preset)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                RenderSettings.sun = sun;
            }
            Vector3 euler = preset.SunEuler;
            sun.transform.rotation = Quaternion.Euler(euler);
            sun.color = preset.SunColor;
            sun.intensity = preset.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = preset.ShadowStrength;
            // Realtime direct light and shadows; with Baked Indirect the probe-volume bake adds the sun's bounce.
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.bounceIntensity = 1f;

            Texture2D cookie = preset.CloudShadowStrength > 0.01f ? BakeCloudCookie(preset.CloudShadowStrength) : null;
            sun.cookie = cookie;
            UniversalAdditionalLightData data = sun.GetUniversalAdditionalLightData();
            data.lightCookieSize = new Vector2(preset.CloudScale, preset.CloudScale);
            data.lightCookieOffset = Vector2.zero;

            var flare = sun.GetComponent<LensFlareComponentSRP>() ?? sun.gameObject.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = BakeSunFlare();
            flare.intensity = preset.FlareIntensity;
            flare.scale = 1f;
            flare.useOcclusion = true;
            flare.occlusionRadius = 0.02f;
            flare.sampleCount = 24;
            flare.allowOffScreen = false;
            flare.enabled = preset.FlareIntensity > 0f;

            var rig = sun.GetComponent<CourseSunRig>() ?? sun.gameObject.AddComponent<CourseSunRig>();
            rig.CourseEuler = euler;
            rig.CourseColor = preset.SunColor;
            rig.CourseIntensity = preset.SunIntensity;
            rig.CourseShadowStrength = preset.ShadowStrength;
            rig.ShopEuler = new Vector3(48f, -35f, 0f);
            rig.ShopColor = new Color(1f, 0.96f, 0.88f);
            rig.ShopIntensity = 1.25f;
            rig.ShopShadowStrength = 1f;
            rig.CloudCookie = cookie;
            rig.CloudScale = preset.CloudScale;
            rig.CloudWind = preset.CloudWind;
            return rig;
        }

        /// <summary>The tileable cloud-shadow cookie as a single-channel texture asset (deterministic, so rebuilds are no-ops).</summary>
        private static Texture2D BakeCloudCookie(float strength)
        {
            Color[] pixels = CloudShadowTexture.Pixels(strength);
            int size = CloudShadowTexture.DefaultSize;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false, true);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(CloudCookiePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(CloudCookiePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(CloudCookiePath);
            importer.textureType = TextureImporterType.SingleChannel;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(CloudCookiePath);
        }

        /// <summary>A restrained photographic sun flare: warm glow and halo at the sun, a few faint ghosts toward the centre.</summary>
        private static LensFlareDataSRP BakeSunFlare()
        {
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(SunFlarePath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
                AssetDatabase.CreateAsset(data, SunFlarePath);
            }
            LensFlareDataElementSRP Element(SRPLensFlareType type, float position, float scale, float intensity, Color tint)
            {
                var e = new LensFlareDataElementSRP
                {
                    flareType = type,
                    position = position,
                    uniformScale = scale,
                    localIntensity = intensity,
                    tint = tint,
                    blendMode = SRPLensFlareBlendMode.Additive,
                    modulateByLightColor = true,
                    preserveAspectRatio = true,
                };
                return e;
            }
            LensFlareDataElementSRP glow = Element(SRPLensFlareType.Circle, 0f, 2.2f, 0.45f, new Color(1f, 0.82f, 0.6f, 1f));
            glow.fallOff = 0.08f;
            LensFlareDataElementSRP core = Element(SRPLensFlareType.Circle, 0f, 0.5f, 0.8f, new Color(1f, 0.95f, 0.85f, 1f));
            core.fallOff = 0.25f;
            LensFlareDataElementSRP halo = Element(SRPLensFlareType.Ring, 0f, 5.5f, 0.05f, new Color(1f, 0.85f, 0.7f, 1f));
            halo.ringThickness = 0.06f;
            halo.fallOff = 0.4f;
            LensFlareDataElementSRP ghostA = Element(SRPLensFlareType.Polygon, 0.55f, 0.35f, 0.06f, new Color(0.7f, 0.9f, 0.8f, 1f));
            ghostA.sideCount = 6;
            ghostA.fallOff = 0.6f;
            ghostA.sdfRoundness = 0.3f;
            LensFlareDataElementSRP ghostB = Element(SRPLensFlareType.Circle, 1.15f, 0.8f, 0.035f, new Color(0.65f, 0.75f, 1f, 1f));
            ghostB.fallOff = 0.85f;
            LensFlareDataElementSRP ghostC = Element(SRPLensFlareType.Polygon, 1.6f, 0.22f, 0.05f, new Color(1f, 0.75f, 0.55f, 1f));
            ghostC.sideCount = 6;
            ghostC.fallOff = 0.5f;
            ghostC.sdfRoundness = 0.3f;
            data.elements = new[] { glow, core, halo, ghostA, ghostB, ghostC };
            EditorUtility.SetDirty(data);
            return data;
        }

        private static void ConfigureRenderSettings(Material sky, CourseLightingPreset preset)
        {
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = preset.AmbientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            RenderSettings.reflectionIntensity = preset.ReflectionIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = preset.FogDensity;
            RenderSettings.fogColor = preset.FogColor;
        }

        private static Volume PlaceVolume(Transform parent, CourseLightingPreset preset)
        {
            var go = new GameObject("Course Post Volume") { layer = 2 };
            go.transform.SetParent(parent, false);
            go.transform.position = VolumeCenter;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = VolumeSize;
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.priority = 1f;
            volume.blendDistance = 0f;
            volume.sharedProfile = BakeVolumeProfile(preset);
            return volume;
        }

        private static VolumeProfile BakeVolumeProfile(CourseLightingPreset preset)
        {
            if (File.Exists(ProfilePath)) AssetDatabase.DeleteAsset(ProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            T Add<T>() where T : VolumeComponent
            {
                T c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            Add<ColorAdjustments>();
            Add<WhiteBalance>();
            Add<SplitToning>();
            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.7f);
            bloom.highQualityFiltering.Override(true);
            var vignette = Add<Vignette>();
            vignette.smoothness.Override(0.45f);
            CourseAtmosphere.ApplyGrade(profile, preset);

            // Edit-mode and fallback focus: only the far coast softens. CourseCameraFocus drives it per camera mode.
            var dof = Add<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(420f);
            dof.gaussianEnd.Override(1400f);
            dof.gaussianMaxRadius.Override(0.5f);
            dof.highQualitySampling.Override(true);
            var flare = Add<ScreenSpaceLensFlare>();
            flare.intensity.Override(0.18f);
            flare.firstFlareIntensity.Override(1f);
            flare.secondaryFlareIntensity.Override(0.6f);
            flare.warpedFlareIntensity.Override(0.4f);
            flare.streaksIntensity.Override(0f);
            flare.chromaticAbberationIntensity.Override(0.4f);
            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            grain.response.Override(0.85f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void PlaceReflectionProbes(Transform parent)
        {
            var group = new GameObject("Reflection Probes").transform;
            group.SetParent(parent, false);
            ReflectionProbe Probe(string name, Vector3 position, Vector3 size, int resolution, int importance)
            {
                var probe = new GameObject(name).AddComponent<ReflectionProbe>();
                probe.transform.SetParent(group, false);
                probe.transform.position = position;
                probe.mode = ReflectionProbeMode.Baked;
                probe.size = size;
                probe.boxProjection = false;
                probe.importance = importance;
                probe.resolution = resolution;
                probe.hdr = true;
                probe.nearClipPlane = 0.5f;
                probe.farClipPlane = 1500f;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                return probe;
            }
            Probe("Course Reflection Probe", new Vector3(-6f, 8f, 80f), new Vector3(520f, 160f, 620f), 256, 1);
            // Sharper, local reflections where the camera sits low over wet-looking turf, the cup and the flag.
            foreach (HoleDefinition hole in GrayboxCourse.Create().Holes)
            {
                Vector3 cup = hole.CupM.ToUnity();
                ReflectionProbe probe = Probe($"Green Reflection Probe {hole.Number:00}", cup + Vector3.up * 2.5f, new Vector3(90f, 40f, 90f), 512, 2);
                probe.blendDistance = 12f;
            }
        }

        /// <summary>
        /// Adaptive probe volume over the playable holes plus a margin, from the beach to well above the clifftops. The
        /// Pro Shop (x ~ 2500) stays outside, so it keeps skybox ambient under its own sun. Finest probes 3 m apart.
        /// </summary>
        private static void PlaceProbeVolume(Transform parent)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (HoleDefinition hole in GrayboxCourse.Create().Holes)
            {
                var world = new AnalyticWorld(hole);
                double hx0 = double.MaxValue, hz0 = double.MaxValue, hx1 = double.MinValue, hz1 = double.MinValue;
                foreach (var p in hole.PlayableBoundary)
                {
                    hx0 = System.Math.Min(hx0, p.x);
                    hx1 = System.Math.Max(hx1, p.x);
                    hz0 = System.Math.Min(hz0, p.z);
                    hz1 = System.Math.Max(hz1, p.z);
                }
                minX = Mathf.Min(minX, (float)hx0);
                maxX = Mathf.Max(maxX, (float)hx1);
                minZ = Mathf.Min(minZ, (float)hz0);
                maxZ = Mathf.Max(maxZ, (float)hz1);
                for (double x = hx0; x <= hx1; x += 10)
                {
                    for (double z = hz0; z <= hz1; z += 10)
                    {
                        float h = (float)world.GroundHeight(x, z);
                        minY = Mathf.Min(minY, h);
                        maxY = Mathf.Max(maxY, h);
                    }
                }
            }
            minY = Mathf.Min(minY, -12f) - 4f;
            maxY += 40f;
            var go = new GameObject("Course Probe Volume");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
            var volume = go.AddComponent<ProbeVolume>();
            volume.mode = ProbeVolume.Mode.Local;
            volume.size = new Vector3(maxX - minX + ProbeVolumeMargin * 2f, maxY - minY, maxZ - minZ + ProbeVolumeMargin * 2f);
            volume.overridesSubdivLevels = true;
            volume.lowestSubdivLevelOverride = 1;
            volume.highestSubdivLevelOverride = 3;
            volume.fillEmptySpaces = false;
        }

        private static void PlaceAtmosphere(Transform parent, CourseLightingPreset preset, CourseSunRig sun, Volume volume)
        {
            var atmosphere = new GameObject("Atmosphere").AddComponent<CourseAtmosphere>();
            atmosphere.transform.SetParent(parent, false);
            atmosphere.Preset = preset;
            atmosphere.Sun = sun;
            atmosphere.CourseVolume = volume;
        }

        private static void ConfigureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cam.allowHDR = true;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            // TAA would need motion vectors from every swaying shader (Foliage has them; scatter grass shaders must too).
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
            data.renderShadows = true;
            data.requiresDepthOption = CameraOverrideOption.On;
            if (cam.GetComponent<GolfCameraRig>() != null && cam.GetComponent<CourseCameraFocus>() == null) cam.gameObject.AddComponent<CourseCameraFocus>();
        }

        /// <summary>
        /// Shadows reach across the hole and down the cliffs; the shop sits well inside the first cascade. The GPU
        /// Resident Drawer batches every DOTS-instanced renderer (all course shaders and URP/Lit) and culls hidden ones
        /// on the GPU; it needs the BatchRendererGroup shader variants kept in builds.
        /// </summary>
        private static void ConfigurePipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (asset == null) return;
            var so = new SerializedObject(asset);
            void Set(SerializedObject target, string name, System.Action<SerializedProperty> write)
            {
                SerializedProperty p = target.FindProperty(name);
                if (p != null) write(p);
                else Debug.LogWarning($"[Puttshack] {target.targetObject.name} has no {name}.");
            }
            Set(so, "m_ShadowDistance", p => p.floatValue = 220f);
            Set(so, "m_ShadowCascadeCount", p => p.intValue = 4);
            Set(so, "m_Cascade4Split", p => p.vector3Value = new Vector3(0.04f, 0.12f, 0.34f));
            Set(so, "m_MainLightShadowmapResolution", p => p.intValue = 4096);
            Set(so, "m_RequireDepthTexture", p => p.boolValue = true);
            // Nothing samples the opaque colour copy; skipping it saves a full-screen copy every frame.
            Set(so, "m_RequireOpaqueTexture", p => p.boolValue = false);
            Set(so, "m_SoftShadowsSupported", p => p.boolValue = true);
            Set(so, "m_ColorGradingMode", p => p.intValue = (int)ColorGradingMode.HighDynamicRange);
            Set(so, "m_ColorGradingLutSize", p => p.intValue = 64);
            Set(so, "m_SupportsLightCookies", p => p.boolValue = true);
            Set(so, "m_SupportDataDrivenLensFlare", p => p.boolValue = true);
            Set(so, "m_SupportScreenSpaceLensFlare", p => p.boolValue = true);
            Set(so, "m_GPUResidentDrawerMode", p => p.intValue = GpuResidentDrawerInstanced);
            Set(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", p => p.boolValue = true);
            Set(so, "m_LightProbeSystem", p => p.intValue = (int)LightProbeSystem.ProbeVolumes);
            Set(so, "m_ProbeVolumeSHBands", p => p.intValue = (int)ProbeVolumeSHBands.SphericalHarmonicsL1);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            Set(graphics, "m_BrgStripping", p => p.intValue = BrgStrippingKeepAll);
            graphics.ApplyModifiedPropertiesWithoutUndo();

            ConfigureRenderer();
        }

        /// <summary>Half-resolution SSAO with a wider, stronger contact shadow; the height-fog feature before post.</summary>
        private static void ConfigureRenderer()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null) return;
            HeightFogFeature fog = null;
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature == null) continue;
                if (feature is HeightFogFeature f) fog = f;
                if (feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var ssao = new SerializedObject(feature);
                ssao.FindProperty("m_Settings.Intensity").floatValue = 0.6f;
                ssao.FindProperty("m_Settings.Radius").floatValue = 0.8f;
                ssao.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.25f;
                ssao.FindProperty("m_Settings.Downsample").boolValue = true;
                ssao.FindProperty("m_Settings.Falloff").floatValue = 120f;
                ssao.ApplyModifiedPropertiesWithoutUndo();
            }

            var material = new Material(Shader.Find("Hidden/Puttshack/HeightFog")) { name = "HeightFog" };
            material = Persist(material);
            if (fog == null)
            {
                fog = ScriptableObject.CreateInstance<HeightFogFeature>();
                fog.name = "HeightFog";
                AssetDatabase.AddObjectToAsset(fog, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(fog, out string _, out long localId);
                var so = new SerializedObject(data);
                SerializedProperty list = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fog;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            fog.Material = material;
            fog.Event = RenderPassEvent.BeforeRenderingPostProcessing;
            fog.SetActive(true);
            EditorUtility.SetDirty(fog);
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// Environment bake: the skybox ambient probe and the reflection probes go into the scene's lighting data.
        /// With <paramref name="bakeGI"/> the course probe volume is baked too (sun bounce and sky occlusion from every
        /// ContributeGI renderer; Baked Indirect keeps the sun's direct light and shadows realtime).
        /// </summary>
        private static void BakeLighting(Scene scene, bool bakeGI)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "Course_Lighting" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }
            settings.bakedGI = bakeGI;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 256;
            settings.environmentSampleCount = 256;
            settings.maxBounces = 2;
            settings.indirectScale = 1f;
            settings.albedoBoost = 1f;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            if (!EditorSceneManager.SaveScene(scene, CourseBaker.ScenePath)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
            if (!Lightmapping.Bake()) Debug.LogWarning("[Puttshack] Environment lighting bake did not complete.");
            if (bakeGI && !EditorSceneManager.SaveScene(scene, CourseBaker.ScenePath)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
        }

        [MenuItem("Puttshack/Bake Course GI (Probe Volume)", priority = 4)]
        public static void BakeCourseGIMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BakeCourseGI();
        }

        /// <summary>Bakes the course probe volume into Main.unity (run after Build Main Scene). Batch: -executeMethod ...BakeCourseGI.</summary>
        public static void BakeCourseGI()
        {
            Scene scene = EditorSceneManager.OpenScene(CourseBaker.ScenePath, OpenSceneMode.Single);
            BakeLighting(scene, true);
            Debug.Log("[Puttshack] Course GI bake finished.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
