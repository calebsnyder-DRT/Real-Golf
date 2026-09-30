using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// Import rules for the models authored in Blender for this project (the trees,
    /// bush, flowering plant, rock cluster, clubhouse, bridge and hole complex under
    /// Models/Authored).
    ///
    /// Those FBXs carry geometry, UVs and vertex colour but no textures: their look
    /// was a procedural node graph in Blender, which does not survive FBX export. So
    /// each material arrives with whatever flat colour the exporter guessed. This
    /// rebuilds them on URP Lit and assigns a colour by material name, the same way
    /// <see cref="KenneyModelPostprocessor"/> handles the kit.
    ///
    /// Values are sRGB and sized against the ground albedos MaterialLibrary uses
    /// (fairway 0.130, green 0.150, rough 0.105, deep rough 0.075 linear luminance),
    /// so an imported tree sits in the same light as the turf it stands on.
    /// </summary>
    public sealed class AuthoredModelPostprocessor : AssetPostprocessor
    {
        private const string AuthoredRoot = "Assets/_Game/Resources/Models/Authored/";

        /// <summary>Material name, base colour, smoothness.</summary>
        private static readonly (string Name, Color Color, float Smoothness)[] Palette =
        {
            // Foliage. Shares its tuning with the Kenney palette so authored and kit
            // trees can stand in the same tree line without reading as two species.
            ("M_Leaves", new Color(0.245f, 0.345f, 0.165f), 0.06f),
            ("M_Bark",   new Color(0.300f, 0.222f, 0.155f), 0.05f),
            ("M_Leaf",   new Color(0.265f, 0.360f, 0.175f), 0.08f),
            ("M_Stem",   new Color(0.250f, 0.235f, 0.140f), 0.06f),
            ("M_Flower", new Color(0.760f, 0.310f, 0.360f), 0.12f),

            // Stone and rock.
            ("M_Granite", new Color(0.420f, 0.415f, 0.400f), 0.08f),
            ("M_Stone",   new Color(0.470f, 0.455f, 0.425f), 0.10f),
            ("M_Rock",    new Color(0.430f, 0.415f, 0.385f), 0.08f),

            // Clubhouse shell.
            ("M_Stucco", new Color(0.720f, 0.690f, 0.610f), 0.06f),
            ("M_Roof",   new Color(0.175f, 0.180f, 0.190f), 0.14f),
            ("M_Timber", new Color(0.330f, 0.230f, 0.150f), 0.08f),
            ("M_Trim",   new Color(0.640f, 0.610f, 0.545f), 0.08f),
            ("M_Frame",  new Color(0.140f, 0.130f, 0.120f), 0.20f),
            ("M_Glass",  new Color(0.120f, 0.145f, 0.170f), 0.85f),
            ("M_Door",   new Color(0.155f, 0.120f, 0.095f), 0.16f),

            // Hole complex: turf plug, white flagstick, black ferrules, red pennant.
            ("M_Turf",  new Color(0.235f, 0.330f, 0.150f), 0.05f),
            ("M_Pole",  new Color(0.880f, 0.880f, 0.875f), 0.35f),
            ("M_Metal", new Color(0.100f, 0.100f, 0.105f), 0.45f),
            ("M_Flag",  new Color(0.780f, 0.180f, 0.150f), 0.10f),

            // Kit surfaces, present on the bridge and any tile pieces brought over.
            ("M_TurfSide", new Color(0.215f, 0.300f, 0.140f), 0.05f),
            ("M_Green",    new Color(0.270f, 0.375f, 0.165f), 0.06f),
            ("M_Rough",    new Color(0.195f, 0.275f, 0.125f), 0.05f),
            ("M_Soil",     new Color(0.230f, 0.175f, 0.130f), 0.05f),
            ("M_Sand",     new Color(0.735f, 0.680f, 0.560f), 0.06f),
            ("M_Water",    new Color(0.090f, 0.240f, 0.320f), 0.92f),
        };

        private bool IsAuthoredAsset => assetPath.Replace('\\', '/').StartsWith(AuthoredRoot);

        /// <summary>Bump when the palette or import rules change, to force a reimport.</summary>
        public override uint GetVersion() => 7;

        private static bool TryGetPaletteEntry(string materialName, out Color color, out float smoothness)
        {
            foreach ((string name, Color value, float smooth) in Palette)
            {
                if (string.Equals(name, materialName, System.StringComparison.OrdinalIgnoreCase))
                {
                    color = value;
                    smoothness = smooth;
                    return true;
                }
            }

            color = default;
            smoothness = 0.1f;
            return false;
        }

        /// <summary>Writes albedo under both the URP and built-in property names.</summary>
        private static void SetBaseColor(Material material, Color color)
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

        private void OnPreprocessModel()
        {
            if (!IsAuthoredAsset)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            // Exported from Blender with "Apply Transform" so the Z-up -> Y-up
            // conversion is baked into the vertex data rather than left on a node
            // transform. That bake also writes the positions in the FBX's own unit
            // (centimetres), which the file declares honestly, so Unity's unit
            // conversion is what turns them back into metres. Both flags matter:
            // useFileScale off here would import everything 100x oversized.
            importer.globalScale = 1f;
            importer.useFileScale = true;

            // The exporter wrote per-face smooth flags deliberately: the rocks and the
            // clubhouse are faceted, the foliage is smooth. Keep them.
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            // Vegetation is placed in its hundreds per hole and never collided with;
            // the clubhouse and bridge are walked into, so they get colliders.
            bool structure = assetPath.Replace('\\', '/').Contains("/Structures/");
            importer.addCollider = structure;
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsAuthoredAsset)
            {
                return;
            }

            // Shader.Find comes back null during batch imports, and a material with no
            // shader has no properties to write, so the palette silently did nothing.
            // The active render pipeline knows its own default shader without a lookup.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? GraphicsSettings.currentRenderPipeline?.defaultShader;
            if (shader != null)
            {
                material.shader = shader;
            }
            else
            {
                Debug.LogWarning($"[AuthoredModelPostprocessor] No URP Lit shader available while " +
                                 $"importing {assetPath}; '{description.materialName}' keeps its " +
                                 "imported shader.");
            }

            // Fall back to whatever the FBX carried, then override by name below.
            if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
            {
                SetBaseColor(material, new Color(diffuse.x, diffuse.y, diffuse.z, 1f));
            }

            float smoothness = 0.1f;
            if (TryGetPaletteEntry(description.materialName, out Color palette, out float paletteSmoothness))
            {
                SetBaseColor(material, palette);
                smoothness = paletteSmoothness;
            }
            else
            {
                Debug.LogWarning($"[AuthoredModelPostprocessor] No palette entry for material " +
                                 $"'{description.materialName}' in {assetPath}. Add one so it does " +
                                 "not import at the exporter's guessed colour.");
            }

            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", 0f);
        }
    }
}
