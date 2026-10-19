using Puttshack.Runtime.Shop;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Course atmosphere from a <see cref="CourseLightingPreset"/>: feeds the full-screen height fog
    /// (Rendering.HeightFogFeature) every frame, and on <see cref="Apply"/> moves the sun, re-tints the sky, fog and
    /// course grade and regenerates the cloud-shadow cookie. The fog is off while the Pro Shop is open (CourseSunRig
    /// hands the shop its own daylight). Runs in edit mode so the Scene view and baker captures show the fog.
    /// </summary>
    [ExecuteAlways]
    public sealed class CourseAtmosphere : MonoBehaviour
    {
        public CourseLightingPreset Preset;
        public CourseSunRig Sun;
        public Volume CourseVolume;

        /// <summary>True while a course atmosphere wants the height fog drawn this frame.</summary>
        public static bool FogActive { get; private set; }

        private static readonly int FogColorId = Shader.PropertyToID("_PuttshackFogColor");
        private static readonly int FogSunColorId = Shader.PropertyToID("_PuttshackFogSunColor");
        private static readonly int FogParamsId = Shader.PropertyToID("_PuttshackFogParams");
        private static readonly int FogParams2Id = Shader.PropertyToID("_PuttshackFogParams2");
        private static readonly int FogSunDirId = Shader.PropertyToID("_PuttshackFogSunDir");

        private ProShopController _shop;
        private Material _runtimeSky;
        private Texture2D _runtimeCookie;

        private void OnEnable()
        {
            _shop = FindAnyObjectByType<ProShopController>(FindObjectsInactive.Include);
            PushFog();
        }

        private void OnDisable()
        {
            FogActive = false;
            if (_runtimeCookie != null) DestroyImmediate(_runtimeCookie);
            if (_runtimeSky != null) DestroyImmediate(_runtimeSky);
        }

        private void LateUpdate() => PushFog();

        /// <summary>Switches the course to another time of day at runtime (sun, sky tint, fog, grade, cloud shadows).</summary>
        public void Apply(CourseLightingPreset preset)
        {
            if (preset == null) return;
            Preset = preset;
            if (Sun != null)
            {
                Sun.CourseEuler = preset.SunEuler;
                Sun.CourseColor = preset.SunColor;
                Sun.CourseIntensity = preset.SunIntensity;
                Sun.CourseShadowStrength = preset.ShadowStrength;
                Sun.CloudScale = preset.CloudScale;
                Sun.CloudWind = preset.CloudWind;
                if (Application.isPlaying)
                {
                    if (_runtimeCookie != null) Destroy(_runtimeCookie);
                    _runtimeCookie = preset.CloudShadowStrength > 0.01f ? CloudShadowTexture.Create(preset.CloudShadowStrength) : null;
                    Sun.CloudCookie = _runtimeCookie;
                }
                Sun.Refresh();
            }

            RenderSettings.ambientIntensity = preset.AmbientIntensity;
            RenderSettings.reflectionIntensity = preset.ReflectionIntensity;
            RenderSettings.fogColor = preset.FogColor;
            RenderSettings.fogDensity = preset.FogDensity;
            if (Application.isPlaying && RenderSettings.skybox != null)
            {
                if (_runtimeSky == null)
                {
                    _runtimeSky = new Material(RenderSettings.skybox) { name = RenderSettings.skybox.name + " (Runtime)" };
                    RenderSettings.skybox = _runtimeSky;
                }
                ApplySky(_runtimeSky, preset);
                DynamicGI.UpdateEnvironment();
            }
            if (Application.isPlaying && CourseVolume != null) ApplyGrade(CourseVolume.profile, preset);
            PushFog();
        }

        public static void ApplySky(Material sky, CourseLightingPreset preset)
        {
            if (preset.SkyTexture != null) sky.SetTexture("_MainTex", preset.SkyTexture);
            sky.SetFloat("_Exposure", preset.SkyExposure);
            sky.SetFloat("_Rotation", preset.SunAzimuth - preset.SkySunAzimuth);
            sky.SetColor("_HazeColor", preset.HazeColor);
            sky.SetColor("_HazeSunColor", preset.HazeSunColor);
            sky.SetFloat("_HazeHeight", preset.HazeHeight);
            sky.SetFloat("_HazeStrength", preset.HazeStrength);
            sky.SetVector("_SunDirection", preset.TowardSun);
        }

        /// <summary>Writes the preset's grade into the course profile's colour, bloom and vignette overrides.</summary>
        public static void ApplyGrade(VolumeProfile profile, CourseLightingPreset preset)
        {
            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(preset.PostExposure);
                color.contrast.Override(preset.Contrast);
                color.saturation.Override(preset.Saturation);
            }
            if (profile.TryGet(out WhiteBalance white))
            {
                white.temperature.Override(preset.Temperature);
                white.tint.Override(preset.Tint);
            }
            if (profile.TryGet(out SplitToning split))
            {
                split.shadows.Override(preset.SplitShadows);
                split.highlights.Override(preset.SplitHighlights);
                split.balance.Override(preset.SplitBalance);
            }
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.intensity.Override(preset.BloomIntensity);
                bloom.tint.Override(preset.BloomTint);
            }
            if (profile.TryGet(out Vignette vignette)) vignette.intensity.Override(preset.VignetteIntensity);
        }

        private void PushFog()
        {
            CourseLightingPreset p = Preset;
            bool shopOpen = _shop != null && _shop.IsOpen;
            FogActive = p != null && isActiveAndEnabled && !shopOpen && p.HeightFogDensity > 0f;
            if (p == null) return;
            Light sun = RenderSettings.sun;
            Vector3 toward = sun != null ? -sun.transform.forward : p.TowardSun;
            Shader.SetGlobalVector(FogColorId, p.HeightFogColor);
            Shader.SetGlobalVector(FogSunColorId, new Vector4(p.HeightFogSunColor.r, p.HeightFogSunColor.g, p.HeightFogSunColor.b, p.HeightFogSunExponent));
            Shader.SetGlobalVector(FogParamsId, new Vector4(p.HeightFogDensity, Mathf.Max(p.HeightFogFalloff, 1e-4f), p.HeightFogBaseHeight, p.HeightFogStart));
            Shader.SetGlobalVector(FogParams2Id, new Vector4(p.HeightFogMaxOpacity, p.HeightFogSkyAmount, 3000f, 0f));
            Shader.SetGlobalVector(FogSunDirId, toward);
        }
    }
}
