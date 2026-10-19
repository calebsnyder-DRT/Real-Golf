using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// One time of day for the course: sun, sky, ambient, distance and height fog, cloud shadows and the grade on the
    /// course post volume. CourseEnvironmentBaker bakes Main.unity from the active preset; <see cref="CourseAtmosphere"/>
    /// applies the runtime half (sun, fog, cloud shadows) and can switch presets in play. The defaults are golden hour.
    /// </summary>
    [CreateAssetMenu(menuName = "Puttshack/Course Lighting Preset", fileName = "CourseLighting")]
    public sealed class CourseLightingPreset : ScriptableObject
    {
        [Header("Sun")]
        [Tooltip("Degrees, atan2(x, z) of the direction toward the sun.")]
        public float SunAzimuth = -105f;
        public float SunElevation = 20f;
        public Color SunColor = new Color(1f, 0.74f, 0.5f);
        public float SunIntensity = 3.3f;
        [Range(0f, 1f)] public float ShadowStrength = 0.9f;
        [Tooltip("Scale of the sun's lens flare (0 = none).")]
        public float FlareIntensity = 1f;

        [Header("Sky")]
        public Texture SkyTexture;
        [Tooltip("Azimuth of the sun in the sky texture; the sky is turned so its glow sits behind the light.")]
        public float SkySunAzimuth = 40.3f;
        public float SkyExposure = 1.15f;
        public Color HazeColor = new Color(0.8f, 0.78f, 0.74f);
        public Color HazeSunColor = new Color(1f, 0.84f, 0.66f);
        public float HazeHeight = 0.09f;
        public float HazeStrength = 0.8f;
        public float AmbientIntensity = 0.72f;
        public float ReflectionIntensity = 1f;

        [Header("Distance fog (built-in, exp2)")]
        public Color FogColor = new Color(0.8f, 0.78f, 0.74f);
        public float FogDensity = 0.0008f;

        [Header("Height fog (full screen, sun in-scattering)")]
        public Color HeightFogColor = new Color(0.78f, 0.77f, 0.75f);
        [ColorUsage(false, true)] public Color HeightFogSunColor = new Color(1.1f, 0.72f, 0.42f);
        [Tooltip("Extinction per metre at the base height.")]
        public float HeightFogDensity = 0.0011f;
        [Tooltip("Density falls by e every 1/falloff metres above the base height.")]
        public float HeightFogFalloff = 0.03f;
        public float HeightFogBaseHeight = -10f;
        public float HeightFogStart = 25f;
        [Range(0f, 1f)] public float HeightFogMaxOpacity = 0.85f;
        [Tooltip("Sharpness of the forward-scattering glow around the sun.")]
        public float HeightFogSunExponent = 8f;
        [Range(0f, 1f)] public float HeightFogSkyAmount = 0.55f;

        [Header("Cloud shadows (sun cookie)")]
        [Range(0f, 1f)] public float CloudShadowStrength = 0.35f;
        [Tooltip("World size of one tile of the cloud texture (m).")]
        public float CloudScale = 700f;
        [Tooltip("Cloud drift (m/s) along world x and z.")]
        public Vector2 CloudWind = new Vector2(2.4f, 1.1f);

        [Header("Grade")]
        public float PostExposure = 0.3f;
        public float Contrast = 20f;
        public float Saturation = 16f;
        public float Temperature = 14f;
        public float Tint = 0f;
        public Color SplitShadows = new Color(0.42f, 0.52f, 0.62f);
        public Color SplitHighlights = new Color(0.78f, 0.62f, 0.46f);
        public float SplitBalance = -12f;
        public float BloomIntensity = 0.45f;
        public Color BloomTint = new Color(1f, 0.9f, 0.78f);
        public float VignetteIntensity = 0.24f;

        public Vector3 TowardSun
        {
            get
            {
                float a = SunAzimuth * Mathf.Deg2Rad, e = SunElevation * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            }
        }

        public Vector3 SunEuler => Quaternion.LookRotation(-TowardSun, Vector3.up).eulerAngles;
    }
}
