using System.Collections.Generic;
using System.IO;
using System.Linq;
using Puttshack.Runtime.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Bakes the in-round HUD (<see cref="HudUIBuilder"/>) into HUD.prefab and places it in Main.unity, where GameHost
    /// finds it. Also owns the import settings of the HUD sprites (ArtSource/UI/HUD/make_hud_ui.py): uncompressed,
    /// no mipmaps, alpha as transparency, and the 9-slice borders below, which mirror BORDERS in the generator.
    /// </summary>
    public static class HudBaker
    {
        public const string SpriteRoot = "Assets/_Game/Art/HUD/Sprites/";
        public const string PrefabDir = "Assets/_Game/Prefabs/HUD";
        public const string PrefabPath = PrefabDir + "/HUD.prefab";
        public const string ObjectName = "HUD";

        /// <summary>9-slice borders in source pixels (left, bottom, right, top).</summary>
        private static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "hud_glass", new Vector4(24, 24, 24, 24) },
            { "hud_card", new Vector4(44, 44, 44, 44) },
            { "hud_card_accent", new Vector4(44, 44, 44, 44) },
            { "hud_plate", new Vector4(14, 14, 14, 14) },
            { "hud_plate_line", new Vector4(14, 14, 14, 14) },
            { "hud_pill", new Vector4(20, 20, 20, 20) },
            { "hud_pill_line", new Vector4(20, 20, 20, 20) },
            { "hud_lane_key", new Vector4(14, 16, 14, 14) },
            { "hud_lane_frame", new Vector4(14, 16, 14, 14) },
            { "hud_lane_glow", new Vector4(38, 38, 38, 38) },
            { "key_blank", new Vector4(22, 26, 22, 22) },
        };

        [MenuItem("Puttshack/HUD/Bake HUD", priority = 60)]
        public static void BakeMenu()
        {
            Bake();
            EditorUtility.DisplayDialog("HUD", $"Baked {PrefabPath}.\nUse Puttshack > HUD > Place HUD in Main Scene to update the scene.", "OK");
        }

        [MenuItem("Puttshack/HUD/Place HUD in Main Scene", priority = 61)]
        public static void PlaceMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Bake();
            PlaceInMainScene();
        }

        public static GameObject Bake()
        {
            ConfigureSprites();
            EnsureFolder(PrefabDir);
            GameObject root = HudUIBuilder.Build();
            try
            {
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok || prefab == null) throw new IOException($"Could not save {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static void PlaceInMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(CourseBaker.ScenePath, OpenSceneMode.Single);
            PlaceInScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
        }

        public static GameObject PlaceInScene(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = Bake();

            foreach (GameObject old in scene.GetRootGameObjects().Where(g => g.GetComponentInChildren<HudView>(true) != null).ToList())
                Object.DestroyImmediate(old);

            var hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            hud.name = ObjectName;
            return hud;
        }

        /// <summary>Applies the HUD sprite import settings; reimports only textures whose settings changed.</summary>
        public static void ConfigureSprites()
        {
            if (!Directory.Exists(SpriteRoot)) throw new DirectoryNotFoundException($"{SpriteRoot} is missing; run ArtSource/UI/HUD/make_hud_ui.py.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string file in Directory.GetFiles(SpriteRoot, "*.png").OrderBy(f => f))
            {
                string path = file.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                Vector4 border = Borders.TryGetValue(name, out Vector4 b) ? b : Vector4.zero;
                bool changed = ti.textureType != TextureImporterType.Sprite
                               || ti.spriteImportMode != SpriteImportMode.Single
                               || !Mathf.Approximately(ti.spritePixelsPerUnit, 100f)
                               || !ti.alphaIsTransparency
                               || ti.alphaSource != TextureImporterAlphaSource.FromInput
                               || !ti.sRGBTexture
                               || ti.mipmapEnabled
                               || ti.wrapMode != TextureWrapMode.Clamp
                               || ti.filterMode != FilterMode.Bilinear
                               || ti.textureCompression != TextureImporterCompression.Uncompressed
                               || ti.maxTextureSize != 1024
                               || ti.spriteBorder != border;
                if (!changed) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 100f;
                ti.alphaIsTransparency = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 1024;
                ti.spriteBorder = border;
                ti.SaveAndReimport();
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
