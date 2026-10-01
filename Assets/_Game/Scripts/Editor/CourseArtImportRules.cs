using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Import rules for the Pebble Beach course art (ArtSource/Blender/scripts/pp_pebble.py):
    /// - Scenery under Art/Course/ and the cypress and tee-marker models under Resources/Models/Course/ import upright
    ///   in metres with Blender's axis turn baked into the mesh, no colliders (the solver never sees scenery) and URP
    ///   Lit materials from the FBX base colour and roughness; PB_Rock* pick up the boulder texture set. PB_Foliage is
    ///   swapped for the Puttshack/Foliage material when the course is baked.
    /// - Ground and foliage layers (rock_/beach_/foliage_ under Resources/Models/Course/Textures/) and the scenery
    ///   textures get normal-map, linear-data and HDR sky settings by file name.
    /// Runs after KenneyModelPostprocessor, which claims everything under Resources/Models/.
    /// </summary>
    public sealed class CourseArtImportRules : AssetPostprocessor
    {
        public const string SceneryRoot = "Assets/_Game/Art/Course/";
        public const string TextureRoot = SceneryRoot + "PebbleBeach/Textures/";
        public const string CourseModelRoot = "Assets/_Game/Resources/Models/Course/";
        public const string GroundTextureRoot = CourseModelRoot + "Textures/";

        public override uint GetVersion() => 1;

        public override int GetPostprocessOrder() => 110;

        private string Path => assetPath.Replace('\\', '/');

        private bool IsModel => Path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                                (Path.StartsWith(SceneryRoot, StringComparison.Ordinal) || Path.StartsWith(CourseModelRoot, StringComparison.Ordinal));

        private void OnPreprocessTexture()
        {
            string file = System.IO.Path.GetFileName(Path);
            bool ground = Path.StartsWith(GroundTextureRoot, StringComparison.Ordinal) &&
                          (file.StartsWith("rock_", StringComparison.Ordinal) || file.StartsWith("beach_", StringComparison.Ordinal) || file.StartsWith("foliage_", StringComparison.Ordinal));
            bool scenery = Path.StartsWith(TextureRoot, StringComparison.Ordinal);
            if (!ground && !scenery) return;

            var importer = (TextureImporter)assetImporter;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.isReadable = false;
            importer.maxTextureSize = 1024;

            if (file.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
            {
                // Equirectangular sky sampled at LOD 0 by Puttshack/HazySky: no mips (they seam at the wrap), clamp at the poles.
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 0;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (file.Contains("_normal"))
            {
                // Poly Haven and the generated ocean maps use the OpenGL (+Y) convention Unity expects.
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                return;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !file.Contains("_noise");
        }

        private void OnPreprocessModel()
        {
            if (!IsModel) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.addCollider = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.indexFormat = ModelImporterIndexFormat.Auto;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsModel) return;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;
            material.shader = lit;
            string name = description.materialName ?? material.name;

            // Blender writes the Principled base colour as linear values; SetColor expects sRGB.
            Color color = Color.white;
            if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse)) color = new Color(diffuse.x, diffuse.y, diffuse.z, 1f).gamma;
            material.SetColor("_BaseColor", color);
            // Blender's exporter writes Shininess = ((1 - roughness) * 10)^2.
            float roughness = 0.7f;
            if (description.TryGetProperty("Shininess", out float shininess) && shininess > 0f) roughness = Mathf.Clamp01(1f - Mathf.Sqrt(shininess / 100f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", Mathf.Clamp(1f - roughness, 0.03f, 0.9f));
            material.SetFloat("_EnvironmentReflections", 1f);

            if (name.StartsWith("PB_Rock", StringComparison.Ordinal))
            {
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "boulder_albedo.png");
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "boulder_normal.png");
                if (albedo != null)
                {
                    material.SetTexture("_BaseMap", albedo);
                    // Cool tints pull the warm boulder scan toward the grey Monterey granite of the cove.
                    material.SetColor("_BaseColor", name == "PB_RockDark" ? new Color(0.46f, 0.47f, 0.5f) : new Color(0.74f, 0.76f, 0.8f));
                }
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 1.2f);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            if (name == "PB_Brass")
            {
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 0.62f);
            }
            if (name == "PB_Window") material.SetFloat("_Smoothness", 0.88f);
        }
    }
}
