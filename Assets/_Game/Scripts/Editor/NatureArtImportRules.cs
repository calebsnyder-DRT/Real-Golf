using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Import rules for the course trees, coast and surf (ArtSource/Blender/scripts/pp_nature.py and pp_coast.py), run
    /// after CourseArtImportRules so they have the last word on these materials:
    /// - PB_Leaf_* / PB_Bark_Tree / PB_Impostor_Trees become Puttshack/TreeFoliage materials on the Art/Course/Nature
    ///   texture atlases, PB_Surf becomes Puttshack/Surf on the scenery foam noise. Other materials are left alone.
    /// - Trees exported as Name_LOD0..Name_LOD3 (LOD3 = impostor cards) get a cross-fading LODGroup.
    /// - Leaf and impostor atlases keep their alpha coverage down the mip chain so canopies don't thin out with distance.
    /// </summary>
    public sealed class NatureArtImportRules : AssetPostprocessor
    {
        public const string NatureTextureRoot = "Assets/_Game/Art/Course/Nature/Textures/";
        private const string FoamNoise = "Assets/_Game/Art/Course/PebbleBeach/Textures/foam_noise.png";
        private const string TreeShader = "Assets/_Game/Shaders/Nature/TreeFoliage.shader";
        private const string SurfShader = "Assets/_Game/Shaders/Nature/Surf.shader";

        /// <summary>Screen-height fractions where LOD0..LOD3 hand over; the impostor culls below the last.</summary>
        private static readonly float[] TreeLodHeights = { 0.25f, 0.1f, 0.03f, 0.004f };

        public override uint GetVersion() => 1;

        public override int GetPostprocessOrder() => 120;

        private string Path => assetPath.Replace('\\', '/');

        private bool IsCourseModel => Path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                                      (Path.StartsWith(CourseArtImportRules.SceneryRoot, StringComparison.Ordinal) ||
                                       Path.StartsWith(CourseArtImportRules.CourseModelRoot, StringComparison.Ordinal));

        private void OnPreprocessTexture()
        {
            if (!Path.StartsWith(NatureTextureRoot, StringComparison.Ordinal)) return;
            string file = System.IO.Path.GetFileNameWithoutExtension(Path);
            var importer = (TextureImporter)assetImporter;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.isReadable = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (file.EndsWith("_Normal", StringComparison.Ordinal))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                return;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !file.EndsWith("_Noise", StringComparison.Ordinal);
            bool cutout = file.StartsWith("Leaf_", StringComparison.Ordinal) || file.StartsWith("Impostor_", StringComparison.Ordinal);
            importer.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (!cutout) return;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.5f;
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsCourseModel) return;
            string name = description.materialName ?? material.name;
            switch (name)
            {
                case "PB_Leaf_Cypress":
                    Leaf(material, "Leaf_Cypress", new Color(0.62f, 0.66f, 0.26f));
                    break;
                case "PB_Leaf_Pine":
                    Leaf(material, "Leaf_Pine", new Color(0.55f, 0.62f, 0.24f));
                    break;
                case "PB_Leaf_Oak":
                    Leaf(material, "Leaf_Oak", new Color(0.7f, 0.72f, 0.24f));
                    break;
                case "PB_Bark_Tree":
                    if (!Tree(material)) return;
                    material.SetTexture("_BaseMap", Texture("Bark_Tree"));
                    material.SetTexture("_BumpMap", Texture("Bark_Tree_Normal"));
                    material.SetFloat("_BumpScale", 1f);
                    material.SetFloat("_Cutoff", 0f);
                    material.SetFloat("_Cull", (float)CullMode.Back);
                    material.SetFloat("_TipStrength", 0f);
                    material.SetFloat("_Translucency", 0f);
                    material.SetFloat("_NormalUp", 0f);
                    material.SetFloat("_Wrap", 0.25f);
                    material.SetFloat("_Smoothness", 0.15f);
                    material.SetFloat("_SpecularStrength", 0.05f);
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.renderQueue = (int)RenderQueue.Geometry;
                    break;
                case "PB_Impostor_Trees":
                    if (!Tree(material)) return;
                    material.SetTexture("_BaseMap", Texture("Impostor_Trees"));
                    // Occlusion and tip tint are baked into the atlas; the cards only sway as a whole.
                    material.SetFloat("_OcclusionStrength", 0f);
                    material.SetFloat("_TipStrength", 0f);
                    material.SetFloat("_Translucency", 0.5f);
                    material.SetFloat("_NormalUp", 0.35f);
                    material.SetFloat("_Flutter", 0f);
                    material.SetFloat("_SpecularStrength", 0.04f);
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    break;
                case "PB_Surf":
                    var surf = AssetDatabase.LoadAssetAtPath<Shader>(SurfShader);
                    context.DependsOnSourceAsset(SurfShader);
                    if (surf == null) return;
                    material.shader = surf;
                    context.DependsOnSourceAsset(FoamNoise);
                    material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FoamNoise));
                    material.renderQueue = -1;
                    break;
            }
        }

        private void Leaf(Material material, string texture, Color translucency)
        {
            if (!Tree(material)) return;
            material.SetTexture("_BaseMap", Texture(texture));
            material.SetColor("_TranslucencyColor", translucency);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.doubleSidedGI = true;
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }

        private bool Tree(Material material)
        {
            context.DependsOnSourceAsset(TreeShader);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(TreeShader);
            if (shader == null) return false;
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            // The Blender materials preview with the same tip tint (pp_nature.TIP_TINT) and occlusion strength.
            material.SetColor("_TipTint", new Color(1.22f, 1.16f, 0.8f));
            material.SetFloat("_OcclusionStrength", 0.8f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.enableInstancing = true;
            return true;
        }

        private Texture2D Texture(string file)
        {
            string path = NatureTextureRoot + file + ".png";
            context.DependsOnSourceAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsCourseModel) return;
            var levels = new List<Renderer>[TreeLodHeights.Length];
            for (int i = 0; i < levels.Length; i++) levels[i] = new List<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    if (renderer.name.EndsWith("_LOD" + i, StringComparison.Ordinal)) levels[i].Add(renderer);
                }
            }
            // Only the four-level trees from pp_nature.py; other LOD'd art keeps the importer's own group.
            if (levels[0].Count == 0 || levels[TreeLodHeights.Length - 1].Count == 0) return;

            var lods = new List<LOD>();
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].Count > 0) lods.Add(new LOD(TreeLodHeights[i], levels[i].ToArray()));
            }
            LODGroup group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }
    }
}
