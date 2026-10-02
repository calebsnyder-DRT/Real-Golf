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
    /// - The golfer characters (Caleb_Modular.fbx, Maya_Modular.fbx) import as Humanoids on the shared golfer skeleton;
    ///   Caleb's file carries the static arms-down "Idle" the Pro Shop poses its mannequins with. Their materials are
    ///   remapped to Art/Characters/Materials by GolferCharacterBaker.
    /// - The Mixamo clips (ArtSource/Mixamo/YBot_TPose, copied to Art/Characters/Animations) import as Humanoid clips on the
    ///   Y Bot skeleton, named after their file, root motion baked into the pose with the feet on the ground, looping
    ///   where they idle; the Humanoid avatars retarget them onto every golfer. Mixamo's golf clips face the target at
    ///   rest and turn side-on to address, so they are turned to the golfer frame (ball at +Z, target at -X).
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
        public const string MayaModel = CharacterRoot + "Maya/Maya_Modular.fbx";
        public const string AnimationRoot = CharacterRoot + "Animations/";

        public static bool IsGolferModel(string path) => path == CalebModel || path == MayaModel;

        public static bool IsMixamoClip(string path) => path.StartsWith(AnimationRoot, StringComparison.Ordinal) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>Clips that cycle: the idles and the walk.</summary>
        public static bool IsLoopingClip(string clipName) => clipName == "Idle" || clipName.StartsWith("Idle_", StringComparison.Ordinal) || clipName == "Walk";

        /// <summary>Yaw added to Mixamo golf clips so their address faces the ball (+Z) and their finish the target (-X).</summary>
        public const float GolfClipYaw = 90f;

        public static bool IsGolfClip(string clipName) => clipName.StartsWith("Golf_", StringComparison.Ordinal);

        /// <summary>Whether a Mixamo clip was imported with the current root settings (bumping the version would reimport every texture).</summary>
        public static bool MixamoClipCurrent(AnimationClip clip)
        {
            AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
            return s.heightFromFeet && !s.keepOriginalPositionY && Mathf.Approximately(s.orientationOffsetY, IsGolfClip(clip.name) ? GolfClipYaw : 0f);
        }

        public override uint GetVersion() => 6;

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
            if (IsMixamoClip(Path))
            {
                PreprocessMixamoClip(importer);
                return;
            }
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            bool golfer = IsGolferModel(Path);
            importer.importBlendShapes = golfer;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;

            // Shop props carry Blender's axis turn in the mesh, so every prefab root imports upright with identity rotation.
            importer.bakeAxisConversion = !golfer;
            importer.importAnimation = golfer;
            if (golfer)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.optimizeGameObjects = false;
                importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
                HumanDescription desc = importer.humanDescription;
                // The reference pose is the file's T-pose bind; a skeleton recorded from an older export would pose the
                // avatar wrongly, so it is rebuilt from the file on every import.
                desc.skeleton = new SkeletonBone[0];
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

        /// <summary>Golfer rig bone map (ArtSource/Blender/scripts/pp_stylized_rig.py PARENTS). Eyes and jaw are not rigged.</summary>
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
                map.Add(($"{side}Toes", $"Toes.{s}"));
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

        /// <summary>Mixamo "without skin" export: the Y Bot skeleton (mixamorig names) and one take.</summary>
        private void PreprocessMixamoClip(ModelImporter importer)
        {
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.bakeAxisConversion = false;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            HumanDescription desc = importer.humanDescription;
            desc.skeleton = new SkeletonBone[0];
            desc.human = MixamoHumanBones(MixamoPrefix(Path));
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

        /// <summary>The bone name prefix Mixamo wrote ("mixamorig:", or "mixamorig7:" for some downloads).</summary>
        private static string MixamoPrefix(string assetPath)
        {
            byte[] bytes = File.ReadAllBytes(assetPath);
            byte[] key = System.Text.Encoding.ASCII.GetBytes("mixamorig");
            for (int i = 0; i + key.Length < bytes.Length; i++)
            {
                int k = 0;
                while (k < key.Length && bytes[i + k] == key[k]) k++;
                if (k < key.Length) continue;
                int end = i + key.Length;
                while (end < bytes.Length && bytes[end] >= '0' && bytes[end] <= '9') end++;
                if (end < bytes.Length && bytes[end] == ':') return System.Text.Encoding.ASCII.GetString(bytes, i, end + 1 - i);
            }
            return "mixamorig:";
        }

        /// <summary>Mixamo's standard skeleton to Unity's human bones.</summary>
        private static HumanBone[] MixamoHumanBones(string prefix)
        {
            var map = new System.Collections.Generic.List<(string Human, string Bone)>
            {
                ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Spine1"), ("UpperChest", "Spine2"), ("Neck", "Neck"), ("Head", "Head"),
            };
            foreach (string side in new[] { "Left", "Right" })
            {
                map.Add(($"{side}Shoulder", $"{side}Shoulder"));
                map.Add(($"{side}UpperArm", $"{side}Arm"));
                map.Add(($"{side}LowerArm", $"{side}ForeArm"));
                map.Add(($"{side}Hand", $"{side}Hand"));
                map.Add(($"{side}UpperLeg", $"{side}UpLeg"));
                map.Add(($"{side}LowerLeg", $"{side}Leg"));
                map.Add(($"{side}Foot", $"{side}Foot"));
                map.Add(($"{side}Toes", $"{side}ToeBase"));
                foreach ((string finger, string bone) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky") })
                {
                    map.Add(($"{side} {finger} Proximal", $"{side}Hand{bone}1"));
                    map.Add(($"{side} {finger} Intermediate", $"{side}Hand{bone}2"));
                    map.Add(($"{side} {finger} Distal", $"{side}Hand{bone}3"));
                }
            }
            var bones = new HumanBone[map.Count];
            for (int i = 0; i < map.Count; i++)
            {
                bones[i] = new HumanBone { humanName = map[i].Human, boneName = prefix + map[i].Bone, limit = new HumanLimit { useDefaultValues = true } };
            }
            return bones;
        }

        private void OnPreprocessAnimation()
        {
            bool mixamo = IsMixamoClip(Path);
            if (Path != CalebModel && Path != MayaModel && !mixamo) return;
            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            if (mixamo) clips = new[] { clips[0] };
            foreach (ModelImporterClipAnimation clip in clips)
            {
                string take = clip.takeName ?? clip.name;
                int bar = take.LastIndexOf('|');
                clip.name = mixamo ? System.IO.Path.GetFileNameWithoutExtension(Path) : bar >= 0 ? take.Substring(bar + 1) : take;
                bool loop = IsLoopingClip(clip.name);
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = !mixamo;
                clip.heightFromFeet = mixamo;
                clip.keepOriginalPositionXZ = true;
                clip.rotationOffset = mixamo && IsGolfClip(clip.name) ? GolfClipYaw : 0f;
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
