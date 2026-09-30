using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Import rules for the Pro Shop and the Caleb character:
    /// - UI sprites (ArtSource/UI/make_proshop_ui.py) become uncompressed 9-sliced sprites; baked icons are plain sprites.
    /// - Blender exports under Art/ProShop/Models and Art/Characters get URP Lit materials from the FBX base colour,
    ///   roughness and metallic; <c>PS_Glass</c> becomes transparent and <c>*_Emit*</c> materials glow.
    /// - Caleb_Modular.fbx imports as a Humanoid with the Idle/Walk/Wave clips renamed and looped.
    /// - The Barlow fonts fall back to Puttshack Display for arrow glyphs.
    /// </summary>
    public sealed class ProShopImportRules : AssetPostprocessor
    {
        public const string ProShopRoot = "Assets/_Game/Art/ProShop/";
        public const string SpriteRoot = ProShopRoot + "UI/Sprites/";
        public const string IconRoot = ProShopRoot + "UI/Icons/";
        public const string FontRoot = ProShopRoot + "UI/Fonts/";
        public const string ModelRoot = ProShopRoot + "Models/";
        public const string CharacterRoot = "Assets/_Game/Art/Characters/";
        public const string CalebModel = CharacterRoot + "Caleb/Caleb_Modular.fbx";

        public override uint GetVersion() => 5;

        private string Path => assetPath.Replace('\\', '/');

        private bool IsModel => Path.StartsWith(ModelRoot, StringComparison.Ordinal) || Path.StartsWith(CharacterRoot, StringComparison.Ordinal);

        private void OnPreprocessTexture()
        {
            bool sprite = Path.StartsWith(SpriteRoot, StringComparison.Ordinal);
            bool icon = Path.StartsWith(IconRoot, StringComparison.Ordinal);
            if (!sprite && !icon) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = icon;
            importer.maxTextureSize = 1024;
            importer.spriteBorder = sprite ? Vector4.one * BorderFor(System.IO.Path.GetFileNameWithoutExtension(Path)) : Vector4.zero;
        }

        /// <summary>Mirrors BORDERS in make_proshop_ui.py (pixels, all four sides).</summary>
        public static float BorderFor(string name)
        {
            switch (name)
            {
                case "ps_chamfer_fill":
                case "ps_chamfer_line":
                case "ps_chamfer_line_thin":
                    return 22f;
                case "ps_chamfer_glow":
                case "ps_paper":
                    return 40f;
                case "ps_soft_fill":
                    return 16f;
                default:
                    return 0f;
            }
        }

        private void OnPreprocessAsset()
        {
            if (!Path.StartsWith(FontRoot, StringComparison.Ordinal) || !Path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) return;
            if (!(assetImporter is TrueTypeFontImporter font)) return;
            font.fontTextureCase = FontTextureCase.Dynamic;
            font.includeFontData = true;
            font.fontRenderingMode = FontRenderingMode.Smooth;
            string file = System.IO.Path.GetFileNameWithoutExtension(Path);
            if (file.StartsWith("BarlowCondensed-", StringComparison.Ordinal))
            {
                // Barlow has no arrows; the hints borrow them from the display face.
                var fallback = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "PuttshackDisplay-ExtraBold.ttf");
                font.fontReferences = fallback != null ? new[] { fallback } : new Font[0];
                // Each weight needs its own first name: weights sharing one resolve to a single cached face, so glyph
                // metrics and bitmaps come from different weights and letters render clipped.
                font.fontNames = new[] { "Barlow Condensed " + file.Substring("BarlowCondensed-".Length), "Puttshack Display" };
            }
        }

        private void OnPreprocessModel()
        {
            if (!IsModel) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;

            bool caleb = Path == CalebModel;
            // Shop props carry Blender's axis turn in the mesh, so every prefab root imports upright with identity rotation.
            importer.bakeAxisConversion = !caleb;
            importer.importAnimation = caleb;
            if (caleb)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.optimizeGameObjects = false;
                HumanDescription desc = importer.humanDescription;
                desc.human = CalebHumanBones();
                desc.upperArmTwist = 0.5f;
                desc.lowerArmTwist = 0.5f;
                desc.upperLegTwist = 0.5f;
                desc.lowerLegTwist = 0.5f;
                desc.armStretch = 0.05f;
                desc.legStretch = 0.05f;
                desc.feetSpacing = 0f;
                desc.hasTranslationDoF = false;
                importer.humanDescription = desc;
            }
            else
            {
                importer.animationType = Path.StartsWith(CharacterRoot, StringComparison.Ordinal) ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            }
        }

        /// <summary>Caleb_Rig bone map (ArtSource/Exports/Caleb_Modular_README.md). Toes, eyes and jaw are not rigged.</summary>
        private static HumanBone[] CalebHumanBones()
        {
            var map = new System.Collections.Generic.List<(string Human, string Bone)>
            {
                ("Hips", "Hips"), ("Spine", "Abdomen"), ("Chest", "Torso"), ("UpperChest", "Chest"), ("Neck", "Neck"), ("Head", "Head"),
            };
            foreach ((string side, string s) in new[] { ("Left", "L"), ("Right", "R") })
            {
                map.Add(($"{side}Shoulder", $"Shoulder.{s}"));
                map.Add(($"{side}UpperArm", $"UpperArm.{s}"));
                map.Add(($"{side}LowerArm", $"LowerArm.{s}"));
                map.Add(($"{side}Hand", $"Wrist.{s}"));
                map.Add(($"{side}UpperLeg", $"UpperLeg.{s}"));
                map.Add(($"{side}LowerLeg", $"LowerLeg.{s}"));
                map.Add(($"{side}Foot", $"Foot.{s}"));
                foreach ((string finger, string bone) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky") })
                {
                    map.Add(($"{side} {finger} Proximal", $"{bone}1.{s}"));
                    map.Add(($"{side} {finger} Intermediate", $"{bone}2.{s}"));
                    map.Add(($"{side} {finger} Distal", $"{bone}3.{s}"));
                }
            }
            var bones = new HumanBone[map.Count];
            for (int i = 0; i < map.Count; i++)
            {
                bones[i] = new HumanBone { humanName = map[i].Human, boneName = map[i].Bone, limit = new HumanLimit { useDefaultValues = true } };
            }
            return bones;
        }

        private void OnPreprocessAnimation()
        {
            if (Path != CalebModel) return;
            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                string take = clip.takeName ?? clip.name;
                int bar = take.LastIndexOf('|');
                clip.name = bar >= 0 ? take.Substring(bar + 1) : take;
                bool loop = clip.name == "Idle" || clip.name == "Walk";
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
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
            else if (description.TryGetProperty("BaseColor", out Vector4 baseColor)) color = new Color(baseColor.x, baseColor.y, baseColor.z, 1f).gamma;
            material.SetColor("_BaseColor", color);

            // Blender's exporter writes Shininess = ((1 - roughness) * 10)^2 and ReflectionFactor = metallic.
            float roughness = 0.6f;
            if (description.TryGetProperty("Shininess", out float shininess) && shininess > 0f) roughness = Mathf.Clamp01(1f - Mathf.Sqrt(shininess / 100f));
            float metallic = 0f;
            if (description.TryGetProperty("ReflectionFactor", out float reflection)) metallic = Mathf.Clamp01(reflection);
            if (metallic < 0.01f && (name.Contains("Brass") || name.Contains("Chrome") || name.Contains("Steel") || name.Contains("Buckle"))) metallic = 0.85f;
            material.SetFloat("_Metallic", metallic);
            // Without baked GI, glossy dielectrics mirror the dark probe; keep wood, plaster and fabric satin at most.
            material.SetFloat("_Smoothness", Mathf.Clamp(1f - roughness, 0.05f, metallic > 0.5f ? 0.9f : 0.5f));
            material.SetFloat("_EnvironmentReflections", 1f);

            if (name.Contains("Glass"))
            {
                MakeTransparent(material, new Color(0.78f, 0.9f, 0.96f, 0.16f));
                material.SetFloat("_Smoothness", 0.95f);
            }
            if (name.Contains("_Emit") || name.Contains("Emissive"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2.2f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        public static void MakeTransparent(Material material, Color color)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetColor("_BaseColor", color);
        }
    }
}
