using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// The Kenney kits ship FBX materials authored for the built-in pipeline, which
    /// import as magenta under URP. This rebuilds each imported material on the URP
    /// Lit shader, carrying over the diffuse colour or colormap texture.
    /// </summary>
    public sealed class KenneyModelPostprocessor : AssetPostprocessor
    {
        private const string KitRoot = "Assets/_Game/Resources/Models/";

        /// <summary>
        /// The Kenney atlas is authored bright: 0.315 mean linear luminance, against
        /// course ground that sits at 0.075-0.15. Left alone the trees and props float
        /// out of the scene under the daylight rig. This tint scales the atlas to
        /// roughly 0.15 so kit models sit in the same light as the turf.
        /// </summary>
        private static readonly Color KitAlbedoTint = new Color(0.72f, 0.72f, 0.72f, 1f);

        /// <summary>
        /// The tree and prop FBXs carry no texture at all -- they are flat materials
        /// coloured straight from DiffuseColor in the kit's stylised palette. Two of
        /// them are badly misnamed: "leafsGreen" is RGB(41, 201, 171), a turquoise,
        /// and "woodBark" is RGB(226, 131, 87), a salmon. Nothing was rendering them
        /// wrongly; the palette simply does not belong on a sunlit course. Because
        /// they are flat colours they can be replaced outright by material name.
        ///
        /// Values are sRGB and share their tuning with MaterialLibrary's tree
        /// palettes, sized against the ground albedos (fairway 0.130, green 0.150,
        /// rough 0.105, deep rough 0.075 linear luminance).
        /// </summary>
        private static readonly (string Name, Color Color)[] KitPalette =
        {
            ("leafsGreen", new Color(0.245f, 0.345f, 0.165f)),
            ("leafsDark", new Color(0.165f, 0.255f, 0.178f)),
            ("woodBark", new Color(0.300f, 0.222f, 0.155f)),
            ("woodBarkDark", new Color(0.238f, 0.176f, 0.128f)),
            ("grass", new Color(0.260f, 0.350f, 0.160f)),
            // The rock props carry their stone under a material called "dirt", and the
            // flower its petals under "colorYellow". Both are Kenney's names, not
            // descriptions of what they shade.
            ("dirt", new Color(0.420f, 0.410f, 0.385f)),
            ("colorYellow", new Color(0.620f, 0.530f, 0.170f))
        };

        /// <summary>
        /// The kit lives directly under Models/. Models/Authored/ holds this project's
        /// own Blender assets, which have their own palette in
        /// <see cref="AuthoredModelPostprocessor"/>; without this exclusion both
        /// postprocessors claim those files and whichever runs last wins, which reset
        /// the authored materials to the exporter's default grey.
        /// </summary>
        private bool IsKitAsset
        {
            get
            {
                string path = assetPath.Replace('\\', '/');
                return path.StartsWith(KitRoot) && !path.StartsWith(KitRoot + "Authored/");
            }
        }

        public override uint GetVersion() => 2;

        private static bool TryGetPaletteColor(string materialName, out Color color)
        {
            foreach ((string name, Color value) in KitPalette)
            {
                if (string.Equals(name, materialName, System.StringComparison.OrdinalIgnoreCase))
                {
                    color = value;
                    return true;
                }
            }

            color = default;
            return false;
        }

        private void OnPreprocessModel()
        {
            if (!IsKitAsset)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.addCollider = false;
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsKitAsset)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return;
            }

            material.shader = shader;

            if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
            {
                var color = new Color(diffuse.x, diffuse.y, diffuse.z, 1f);
                material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color);
                }
            }

            if (description.TryGetProperty("DiffuseColor", out TexturePropertyDescription texture) && texture.texture != null)
            {
                material.SetTexture("_BaseMap", texture.texture);
                material.SetColor("_BaseColor", KitAlbedoTint);
            }

            // Last word, so it overrides whatever the FBX asked for. Only the flat
            // kit palette names match here; the textured golf models are untouched.
            if (TryGetPaletteColor(description.materialName, out Color palette))
            {
                material.SetColor("_BaseColor", palette);
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", palette);
                }
            }

            material.SetFloat("_Smoothness", 0.1f);
            material.SetFloat("_Metallic", 0f);
        }
    }
}
