using RealGolf.Course;
using RealGolf.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RealGolf.Core
{
    /// <summary>
    /// The only component the scene needs. It establishes lighting and sky, then
    /// hands off to <see cref="GameManager"/>, which builds everything else.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Open the course select first. Turn off to drop straight into a " +
                 "round using the PlayerCount and Seed below, which is what the " +
                 "editor preview and the course bake want.")]
        public bool ShowCourseSelect = true;

        [Range(1, 4)] public int PlayerCount = 2;
        public bool RandomiseSeed = true;
        public int Seed = 12345;

        [Header("Lighting")]
        [Tooltip("Warm daylight. Balanced against the turf albedos, which sit at " +
                 "0.075-0.28 linear luminance.")]
        public Color SunColor = new Color(1f, 0.96f, 0.91f);

        [Range(0.4f, 2.5f)]
        [Tooltip("Direct sun, and the dominant term by far: sweeping it moves the " +
                 "fairway 0.209 to 0.280 while ambient over its whole range moves it " +
                 "0.025. Held at 1.0 and the turf albedo brought down to meet it, " +
                 "rather than dimming the sun, so shadows keep their clear-day bite.")]
        public float SunIntensity = 1.0f;

        [Range(0f, 3f)]
        [Tooltip("Scales skybox ambient. A weak lever on the turf -- the whole 0.15 " +
                 "to 0.9 range only shifts it 0.277 to 0.298 -- so it is set for the " +
                 "shadows, which are the only place it really shows.")]
        public float AmbientIntensity = 0.45f;

        [Range(0f, 0.005f)]
        [Tooltip("Exponential-squared fog. 0.0016 gives 15% haze at 250 m and 34% at " +
                 "400 m. The old 0.0024 buried the middle distance: the reference's " +
                 "near woods still read blue-over-green 0.49 at that range, which is " +
                 "a wood that has kept two thirds of its own colour, not a grey ghost.")]
        public float FogDensity = 0.0016f;

        [Tooltip("Tonemapping, grading and bloom on a code-built global volume.")]
        public bool EnablePostProcessing = true;

        private void Awake()
        {
            SetupEnvironment();

            // A baked course is one fixed layout: its meshes were generated from a
            // single seed and cannot describe another, so offering a course list
            // over the top of it would be offering something it cannot deliver.
            bool baked = FindAnyObjectByType<BakedCourse>() != null;

            if (ShowCourseSelect && !baked)
            {
                OpenCourseSelect();
                return;
            }

            if (ShowCourseSelect && baked)
            {
                Debug.Log("[GameBootstrap] Scene has a baked course, so the course " +
                          "select is skipped; that layout is the only one available.");
            }

            StartRound(PlayerCount, Seed, RandomiseSeed, courseName: null);
        }

        private void OpenCourseSelect()
        {
            var menuGo = new GameObject("CourseSelect");
            var menu = menuGo.AddComponent<CourseSelectMenu>();
            menu.Confirmed += selection => StartRound(
                selection.PlayerCount, selection.Seed, randomiseSeed: false, selection.CourseName);
            menu.Build();
        }

        private void StartRound(int players, int seed, bool randomiseSeed, string courseName)
        {
            var managerGo = new GameObject("GameManager");
            var manager = managerGo.AddComponent<GameManager>();
            manager.PlayerCount = players;
            manager.RandomiseSeed = randomiseSeed;
            manager.Seed = seed;
            manager.CourseName = courseName;
        }

        /// <summary>
        /// Sun, sky, fog and grade. Public so an editor preview can light a hole
        /// through the same path the game uses; a copy of these numbers in a tool
        /// would drift from the real look the moment either was touched.
        /// </summary>
        public void SetupEnvironment()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.shadowNormalBias = 0.35f;

            // The sky HDRI has its own sun burnt into it. Located by the luminance
            // -weighted centroid of its brightest pixels, it sits at elevation 29.09
            // and azimuth 216.21 degrees. Unity's panoramic skybox maps
            // u = 0.5 - atan2(z, x) / 2pi, so the matching yaw is azimuth + 90.
            //
            // The yaw is held to that so the light agrees with the bright side of the
            // sky, but the elevation is deliberately raised. At the HDRI's own 29 deg
            // the ground only catches sin(29) = 0.49 of the sun and every tree lays a
            // shadow clean across the fairway; the reference art is a high midday sun
            // with short soft shadows. At 52 deg the ground takes 0.79 instead. The
            // cost is that the sky's own glare sits lower than the shadows imply,
            // which only shows when the camera looks straight down the sun's bearing.
            sunGo.transform.rotation = Quaternion.Euler(52f, 306.2f, 0f);

            RenderSettings.sun = sun;

            Material sky = MaterialLibrary.Skybox;
            if (sky != null)
            {
                // Lighting the scene from the sky itself gives far better bounce
                // and horizon colour than hand-picked ambient bands.
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Skybox;

                // The HDRI averages 0.754 linear over the sphere before the skybox's
                // 1.15 exposure. At 0.41 the sun still leads the sky about 3:1, which
                // is what a clear day reads as; higher and the turf goes grey-blue and
                // loses the yellow-green the albedo pass bought.
                RenderSettings.ambientIntensity = AmbientIntensity;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                // Same measured sky, sampled in three bands and pre-scaled to the
                // ambient level above, because Trilight ignores ambientIntensity.
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.275f, 0.396f, 0.553f);
                RenderSettings.ambientEquatorColor = new Color(0.594f, 0.752f, 0.930f);
                RenderSettings.ambientGroundColor = new Color(0.330f, 0.380f, 0.360f);
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            // Haze converges on the ridges, not on the sky. This was previously set
            // to a bright sky blue, and everything far away climbed to 0.60 rendered
            // luminance -- distant trees came out as white spikes. In the reference
            // nothing hazy ever gets bright: its ridges sit at 0.19-0.25, its far
            // treeline 0.18, its village 0.13, all well under the sky's 0.415. So the
            // fog is aimed at its near-ridge colour and the far scenery is painted to
            // the same value, which lets the two meet without a seam.
            RenderSettings.fogColor = new Color(0.355f, 0.520f, 0.585f);

            // Reads as 6% haze at 100 m, 35% at 250 m and 82% at 500 m. That ramp is
            // taken from the reference, where turf blue-to-green climbs from 0.26 in
            // the foreground to 0.45 at the far bank, and it buries the terrain skirt.
            RenderSettings.fogDensity = FogDensity;

            // Beyond the terrain skirt. The camera is not built yet, so the rings
            // find it themselves on their first frame.
            BackdropBuilder.Build(null, null);

            SetupPostProcessing();
        }

        /// <summary>
        /// Builds the global volume in code so the look travels with the scripts,
        /// the same way the materials do. Without tonemapping the bright sky and
        /// the sand clip hard, which is what makes an otherwise fine course look
        /// like a flat render.
        /// </summary>
        private void SetupPostProcessing()
        {
            if (!EnablePostProcessing)
            {
                return;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "RealGolfGrade";

            // Neutral rather than ACES: ACES pushes the greens toward olive and
            // costs the fairway the colour we just spent the albedo pass keeping.
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            // Sky and turf both measured a consistent 0.89 of the reference art's
            // luminance, which is a single global stop rather than anything local.
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.overrideState = true;
            grade.postExposure.value = 0.32f;
            grade.contrast.overrideState = true;
            grade.contrast.value = 9f;
            grade.saturation.overrideState = true;
            grade.saturation.value = 8f;

            // Held above the sky's own horizon band, which peaks near 1.1 linear. Any
            // lower and the HDRI's solar disc smears across the whole upper frame and
            // the blue goes flat white -- the sky loses more saturation to bloom than
            // any grading can put back.
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.6f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.25f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.16f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.45f;

            var volumeGo = new GameObject("Global Volume (Runtime)");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;
        }
    }
}
