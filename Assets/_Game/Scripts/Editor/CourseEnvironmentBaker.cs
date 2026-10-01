using System.IO;
using Puttshack.Runtime.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Stillwater Cove surroundings and golden-hour lighting for Main.unity, called from CourseBaker.BuildMainScene:
    /// - the Blender scenery (ArtSource/Blender/scripts/pp_pebble.py: surround terrain that continues the gameplay
    ///   ground exactly at its edge, the cove and ocean, rocks, cypress, the Lodge, pier, boats, forest) as a prefab
    ///   with the course ground and ocean materials;
    /// - a low warm sun over the Pacific (CourseSunRig gives the Pro Shop its own daylight), the hazy HDR sky turned
    ///   so its sun glow sits behind the light, skybox ambient, exponential sea haze and a baked reflection probe;
    /// - a course-only post volume (ACES, bloom, warm balance, split toning, vignette) that stops short of the shop,
    ///   camera post-processing and SMAA, and longer cascaded shadows on the PC pipeline asset.
    /// </summary>
    public static class CourseEnvironmentBaker
    {
        public const string Root = "Assets/_Game/Course/Baked/Environment";
        public const string SceneryModel = "Assets/_Game/Art/Course/PebbleBeach/PebbleBeach_Scenery.fbx";
        public const string SceneryPrefab = Root + "/PebbleBeach_Scenery.prefab";
        public const string TextureRoot = "Assets/_Game/Art/Course/PebbleBeach/Textures/";
        public const string ProfilePath = Root + "/Course_PostVolume.asset";
        public const string LightingSettingsPath = Root + "/Course_Lighting.lighting";
        public const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";

        /// <summary>
        /// Sun azimuth (deg, atan2(x, z) of the direction toward the sun): low over the sea behind the player's left
        /// shoulder, so the cove is lit from behind the tee and the fairway gets long cross shadows.
        /// </summary>
        public const float SunAzimuth = -105f;
        public const float SunElevation = 20f;
        /// <summary>Azimuth of the sun in pebble_sky.hdr (u = 0.612 in the equirectangular image).</summary>
        public const float SkySunAzimuth = 40.3f;

        public static readonly Color SunColor = new Color(1f, 0.74f, 0.5f);
        public const float SunIntensity = 3.3f;
        public const float AmbientIntensity = 0.72f;
        public static readonly Color HazeColor = new Color(0.8f, 0.78f, 0.74f);
        public static readonly Color HazeSunColor = new Color(1f, 0.84f, 0.66f);
        public const float FogDensity = 0.0012f;

        /// <summary>Course post volume bounds: everything within ~1.7 km of the hole, but not the Pro Shop at x = 2500.</summary>
        public static readonly Vector3 VolumeCenter = new Vector3(-200f, 0f, 200f);
        public static readonly Vector3 VolumeSize = new Vector3(3200f, 1200f, 3400f);

        public static Vector3 TowardSun
        {
            get
            {
                float a = SunAzimuth * Mathf.Deg2Rad, e = SunElevation * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            }
        }

        public static void PlaceInScene(Scene scene)
        {
            EnsureFolder(Root);
            var root = new GameObject("Environment");
            SceneManager.MoveGameObjectToScene(root, scene);

            Material ground = GroundMaterial();
            Material ocean = OceanMaterial();
            Material sky = SkyMaterial();
            GameObject prefab = BuildSceneryPrefab(ground, ocean);
            var scenery = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            scenery.transform.SetParent(root.transform, false);

            ConfigureSun();
            ConfigureRenderSettings(sky);
            PlaceVolume(root.transform);
            PlaceReflectionProbe(root.transform);
            ConfigureCamera();
            ConfigurePipeline();
            BakeLighting(scene);
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

        private static Material SkyMaterial()
        {
            var m = new Material(Shader.Find("Puttshack/HazySky")) { name = "PebbleSky" };
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "pebble_sky.hdr"));
            m.SetFloat("_Exposure", 1.15f);
            m.SetFloat("_Rotation", SunAzimuth - SkySunAzimuth);
            m.SetColor("_HazeColor", HazeColor);
            m.SetColor("_HazeSunColor", HazeSunColor);
            m.SetFloat("_HazeHeight", 0.09f);
            m.SetFloat("_HazeStrength", 0.8f);
            m.SetVector("_SunDirection", TowardSun);
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
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                }
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, SceneryPrefab, out bool ok);
                if (!ok || prefab == null) throw new IOException($"Could not save {SceneryPrefab}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------------------------------------------------------------- lighting

        private static void ConfigureSun()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                RenderSettings.sun = sun;
            }
            Vector3 euler = Quaternion.LookRotation(-TowardSun, Vector3.up).eulerAngles;
            sun.transform.rotation = Quaternion.Euler(euler);
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            var rig = sun.GetComponent<CourseSunRig>() ?? sun.gameObject.AddComponent<CourseSunRig>();
            rig.CourseEuler = euler;
            rig.CourseColor = SunColor;
            rig.CourseIntensity = SunIntensity;
            rig.CourseShadowStrength = 0.9f;
            rig.ShopEuler = new Vector3(48f, -35f, 0f);
            rig.ShopColor = new Color(1f, 0.96f, 0.88f);
            rig.ShopIntensity = 1.25f;
            rig.ShopShadowStrength = 1f;
        }

        private static void ConfigureRenderSettings(Material sky)
        {
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = AmbientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogColor = HazeColor;
        }

        private static void PlaceVolume(Transform parent)
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
            volume.sharedProfile = BakeVolumeProfile();
        }

        private static VolumeProfile BakeVolumeProfile()
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
            var color = Add<ColorAdjustments>();
            color.postExposure.Override(0.3f);
            color.contrast.Override(20f);
            color.saturation.Override(16f);
            var white = Add<WhiteBalance>();
            white.temperature.Override(14f);
            white.tint.Override(0f);
            var split = Add<SplitToning>();
            split.shadows.Override(new Color(0.42f, 0.52f, 0.62f));
            split.highlights.Override(new Color(0.78f, 0.62f, 0.46f));
            split.balance.Override(-12f);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.78f));
            bloom.highQualityFiltering.Override(true);
            var vignette = Add<Vignette>();
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void PlaceReflectionProbe(Transform parent)
        {
            var probe = new GameObject("Course Reflection Probe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(parent, false);
            probe.transform.position = new Vector3(-6f, 8f, 80f);
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(520f, 160f, 620f);
            probe.boxProjection = false;
            probe.importance = 1;
            probe.resolution = 256;
            probe.hdr = true;
            probe.nearClipPlane = 0.5f;
            probe.farClipPlane = 1500f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        private static void ConfigureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cam.allowHDR = true;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
            data.renderShadows = true;
            data.requiresDepthOption = CameraOverrideOption.On;
        }

        /// <summary>Shadows reach across the hole and down the cliffs; the shop sits well inside the first cascade.</summary>
        private static void ConfigurePipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (asset == null) return;
            var so = new SerializedObject(asset);
            void Set(string name, System.Action<SerializedProperty> write)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p != null) write(p);
            }
            Set("m_ShadowDistance", p => p.floatValue = 220f);
            Set("m_ShadowCascadeCount", p => p.intValue = 4);
            Set("m_Cascade4Split", p => p.vector3Value = new Vector3(0.04f, 0.12f, 0.34f));
            Set("m_MainLightShadowmapResolution", p => p.intValue = 4096);
            Set("m_RequireDepthTexture", p => p.boolValue = true);
            Set("m_SoftShadowsSupported", p => p.boolValue = true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// Environment-only bake (no lightmaps): stores the skybox ambient probe and the course reflection probe in the
        /// scene's lighting data so players get the same light as the editor.
        /// </summary>
        private static void BakeLighting(Scene scene)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "Course_Lighting" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }
            settings.bakedGI = false;
            settings.realtimeGI = false;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            if (!EditorSceneManager.SaveScene(scene, CourseBaker.ScenePath)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
            if (!Lightmapping.Bake()) Debug.LogWarning("[Puttshack] Environment lighting bake did not complete.");
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
