using System.Collections.Generic;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Materials and models shared by the editor baker and the runtime fallback builder, so both produce identical visuals.
    /// </summary>
    public sealed class CourseVisualAssets
    {
        public Material Ground;
        public Material Water;
        public Material CartPath;
        public Material Trunk;
        public Material Canopy;
        public Material FlagPole;
        public Material FlagCloth;
        public Material Cup;
        public Material TeeMarker;
        public Material BoundaryStake;
        public Material PenaltyStake;
        public Material Foliage;
        public readonly List<GameObject> BroadleafModels = new List<GameObject>();
        public readonly List<GameObject> PineModels = new List<GameObject>();
        public readonly List<GameObject> CypressModels = new List<GameObject>();
        public GameObject TeeMarkerModel;

        public const string TextureRoot = "Models/Course/Textures/";

        /// <summary>Name prefix of the Blender canopy materials that <see cref="Foliage"/> replaces.</summary>
        public const string BlenderFoliagePrefix = "PB_Foliage";

        private const string TeeMarkerPath = "Models/Course/TeeMarker/TeeMarker";

        private static readonly string[] BroadleafPaths =
        {
            "Models/Nature/Stylized/Trees/CommonTree_1",
            "Models/Nature/Stylized/Trees/CommonTree_2",
            "Models/Nature/Stylized/Trees/CommonTree_3",
        };

        private static readonly string[] PinePaths =
        {
            "Models/Nature/Stylized/Trees/Pine_1",
            "Models/Nature/Stylized/Trees/Pine_2",
        };

        /// <summary>Monterey cypress prefabs baked from ArtSource/Blender/PuttshackPro_PebbleBeach.blend.</summary>
        private static readonly string[] CypressPaths =
        {
            "Models/Course/Cypress/Cypress_A",
            "Models/Course/Cypress/Cypress_B",
            "Models/Course/Cypress/Cypress_C",
        };

        /// <summary>Creates in-memory materials and loads tree models from Resources.</summary>
        public static CourseVisualAssets CreateDefault()
        {
            var assets = new CourseVisualAssets();

            Shader groundShader = Shader.Find("Puttshack/CourseGround");
            Shader waterShader = Shader.Find("Puttshack/CourseWater");
            Shader foliageShader = Shader.Find("Puttshack/Foliage");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            assets.Ground = new Material(groundShader != null ? groundShader : lit) { name = "CourseGround" };
            if (groundShader != null)
            {
                SetTexture(assets.Ground, "_RoughTex", "rough_albedo");
                SetTexture(assets.Ground, "_FairwayTex", "fairway_albedo");
                SetTexture(assets.Ground, "_GreenTex", "green_albedo");
                SetTexture(assets.Ground, "_BunkerTex", "bunker_albedo");
                SetTexture(assets.Ground, "_DeepRoughTex", "deeprough_albedo");
                SetTexture(assets.Ground, "_TurfNormal", "turf_normal");
                SetTexture(assets.Ground, "_SandNormal", "sand_normal");
                SetTexture(assets.Ground, "_RockTex", "rock_albedo");
                SetTexture(assets.Ground, "_RockNormal", "rock_normal");
                SetTexture(assets.Ground, "_BeachTex", "beach_albedo");
                SetTexture(assets.Ground, "_BeachNormal", "beach_normal");
            }
            else
            {
                SetColor(assets.Ground, new Color(0.38f, 0.6f, 0.25f));
            }

            assets.Water = new Material(waterShader != null ? waterShader : lit) { name = "CourseWater" };
            if (waterShader == null) SetColor(assets.Water, new Color(0.2f, 0.48f, 0.6f));

            assets.CartPath = LitMaterial(lit, "CartPath", new Color(0.72f, 0.70f, 0.64f), 0.2f);
            assets.Trunk = LitMaterial(lit, "Trunk", new Color(0.36f, 0.26f, 0.18f), 0.1f);
            assets.Canopy = LitMaterial(lit, "Canopy", new Color(0.22f, 0.45f, 0.2f), 0.1f);
            assets.FlagPole = LitMaterial(lit, "FlagPole", new Color(0.95f, 0.95f, 0.92f), 0.5f);
            assets.FlagCloth = LitMaterial(lit, "FlagCloth", new Color(0.86f, 0.18f, 0.16f), 0.2f);
            assets.Cup = LitMaterial(lit, "Cup", new Color(0.06f, 0.07f, 0.07f), 0f);
            assets.TeeMarker = LitMaterial(lit, "TeeMarker", new Color(0.16f, 0.24f, 0.19f), 0.45f);
            assets.BoundaryStake = LitMaterial(lit, "BoundaryStake", new Color(0.97f, 0.97f, 0.97f), 0.3f);
            assets.PenaltyStake = LitMaterial(lit, "PenaltyStake", new Color(0.78f, 0.1f, 0.08f), 0.3f);

            if (foliageShader != null)
            {
                assets.Foliage = new Material(foliageShader) { name = "Foliage" };
                SetTexture(assets.Foliage, "_ClumpNormal", "foliage_normal");
                SetTexture(assets.Foliage, "_ClumpNoise", "foliage_noise");
            }
            else
            {
                assets.Foliage = LitMaterial(lit, "Foliage", new Color(0.2f, 0.29f, 0.14f), 0.05f);
            }

            LoadModels(BroadleafPaths, assets.BroadleafModels);
            LoadModels(PinePaths, assets.PineModels);
            LoadModels(CypressPaths, assets.CypressModels);
            assets.TeeMarkerModel = Resources.Load<GameObject>(TeeMarkerPath);
            return assets;
        }

        /// <summary>Replaces the Blender canopy materials on an instantiated model with <see cref="Foliage"/>.</summary>
        public void ApplyFoliage(GameObject instance)
        {
            if (Foliage == null) return;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !materials[i].name.StartsWith(BlenderFoliagePrefix, System.StringComparison.Ordinal)) continue;
                    materials[i] = Foliage;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        public IEnumerable<Material> AllMaterials()
        {
            yield return Ground;
            yield return Water;
            yield return CartPath;
            yield return Trunk;
            yield return Canopy;
            yield return FlagPole;
            yield return FlagCloth;
            yield return Cup;
            yield return TeeMarker;
            yield return BoundaryStake;
            yield return PenaltyStake;
            yield return Foliage;
        }

        public static Material LitMaterial(Shader lit, string name, Color color, float smoothness)
        {
            var m = new Material(lit) { name = name };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void SetTexture(Material m, string property, string textureName)
        {
            var tex = Resources.Load<Texture2D>(TextureRoot + textureName);
            if (tex != null) m.SetTexture(property, tex);
        }

        private static void LoadModels(string[] paths, List<GameObject> into)
        {
            foreach (string path in paths)
            {
                var model = Resources.Load<GameObject>(path);
                if (model != null) into.Add(model);
            }
        }
    }
}
