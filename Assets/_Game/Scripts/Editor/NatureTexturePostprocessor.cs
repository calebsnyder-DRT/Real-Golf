using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Import rules for the photoscanned vegetation under Resources/Models/Nature.
    ///
    /// These assets use the suffixes _diff / _nor / _rough, chosen so they do not
    /// collide with BakedTexturePostprocessor, which claims _albedo.png and
    /// _normal.png across the whole of Resources/Models/. Nothing here touches a
    /// path outside Nature/.
    ///
    /// KenneyModelPostprocessor also claims all of Resources/Models/ and would
    /// otherwise stamp the low-poly kit's flat palette and albedo tint onto these
    /// materials, so this runs after it and has the last word for Nature assets.
    /// </summary>
    public sealed class NatureTexturePostprocessor : AssetPostprocessor
    {
        private const string NatureRoot = "Assets/_Game/Resources/Models/Nature/";
        private const string TextureDir = NatureRoot + "Textures/";

        /// <summary>Foliage is alpha-clipped and drawn from both sides; bark is not.</summary>
        private const float FoliageCutoff = 0.5f;

        public override uint GetVersion() => 1;

        /// <summary>
        /// KenneyModelPostprocessor uses the default order of 0. Nature assets must
        /// be resolved after it so its kit palette cannot overwrite them.
        /// </summary>
        public override int GetPostprocessOrder() => 100;

        private bool IsNatureAsset => assetPath.Replace('\\', '/').StartsWith(NatureRoot);

        private void OnPreprocessTexture()
        {
            if (!IsNatureAsset)
            {
                return;
            }

            string path = assetPath.Replace('\\', '/');
            var importer = (TextureImporter)assetImporter;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;

            if (path.EndsWith("_nor.png"))
            {
                // PolyHaven ships OpenGL-convention normals, which is what Unity wants.
                importer.textureType = TextureImporterType.NormalMap;
                return;
            }

            if (path.EndsWith("_rough.png"))
            {
                importer.textureType = TextureImporterType.SingleChannel;
                importer.sRGBTexture = false;
                return;
            }

            if (path.EndsWith("_diff.png"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                // Leaf sheets carry the cutout in alpha. Marking it as transparency
                // makes Unity dilate colour under the cut texels so mips do not
                // darken the edge of every leaf.
                importer.alphaIsTransparency = true;
            }
        }

        private void OnPreprocessModel()
        {
            if (!IsNatureAsset)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;                    // authored in metres
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        private static Texture2D LoadMap(string materialName, string suffix)
        {
            string path = TextureDir + materialName + suffix + ".png";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool DiffuseHasAlpha(string materialName)
        {
            string path = TextureDir + materialName + "_diff.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer != null && importer.DoesSourceTextureHaveAlpha();
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsNatureAsset)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return;
            }

            material.shader = shader;

            // Material names in the FBX match the texture set names exactly, e.g.
            // "jacaranda_tree_leaves" -> jacaranda_tree_leaves_diff.png.
            string name = description.materialName;

            Texture2D baseMap = LoadMap(name, "_diff");
            if (baseMap != null)
            {
                material.SetTexture("_BaseMap", baseMap);
            }

            // Full white: the grading is baked into the texture, so a tint here would
            // double-apply it.
            material.SetColor("_BaseColor", Color.white);

            Texture2D normal = LoadMap(name, "_nor");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }

            material.SetFloat("_Metallic", 0f);
            // URP Lit reads smoothness from a packed map's alpha, and these diffuse
            // alphas are cutouts, so the shipped _rough maps cannot be bound directly.
            // Constants stand in until someone packs a MetallicGloss map.
            bool foliage = DiffuseHasAlpha(name);
            material.SetFloat("_Smoothness", foliage ? 0.18f : 0.08f);

            if (foliage)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", FoliageCutoff);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                // Leaf cards are single-sided geometry and must be lit from behind.
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                material.doubleSidedGI = true;
            }
        }

        /// <summary>
        /// Each FBX holds three sibling meshes named &lt;asset&gt;_LOD0/1/2. Wire them
        /// into an LODGroup so a hole can scatter hundreds of trees without paying
        /// LOD0 for all of them.
        /// </summary>
        private void OnPostprocessModel(GameObject root)
        {
            if (!IsNatureAsset)
            {
                return;
            }

            var levels = new System.Collections.Generic.List<Renderer>[3];
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = new System.Collections.Generic.List<Renderer>();
            }

            bool found = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    if (renderer.name.EndsWith("_LOD" + i))
                    {
                        levels[i].Add(renderer);
                        found = true;
                    }
                }
            }

            if (!found || levels[0].Count == 0)
            {
                return;
            }

            // Tuned for a course where trees are read at 20-200 m.
            float[] heights = { 0.22f, 0.075f, 0.012f };
            var lods = new System.Collections.Generic.List<LOD>();
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].Count > 0)
                {
                    lods.Add(new LOD(heights[i], levels[i].ToArray()));
                }
            }

            LODGroup group = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }
    }
}
