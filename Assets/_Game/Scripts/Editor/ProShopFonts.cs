using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Static TextMeshPro SDF font assets for the Pro Shop UI, baked from the TTFs in <see cref="ProShopImportRules.FontRoot"/>.
    /// Static atlases never re-rasterise at runtime, so labels stay sharp at any canvas scale and can't render stale
    /// glyphs the way dynamic legacy fonts do. Existing assets are reused; <c>Puttshack > Pro Shop > Rebuild Fonts</c>
    /// regenerates them after changing the TTFs or the character set.
    /// </summary>
    public static class ProShopFonts
    {
        public const string AssetRoot = ProShopImportRules.FontRoot + "SDF/";

        private const string Ascii =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        private const string Punctuation = "\u00b7\u2013\u2014\u2018\u2019\u201c\u201d\u2026\u2022\u00d7\u00b0";
        private const string Arrows = "\u2190\u2191\u2192\u2193";

        private const int SamplingSize = 84;
        private const int Padding = 9;

        public static TMP_FontAsset Display => Load("PuttshackSlabCondensed-Black");
        public static TMP_FontAsset ExtraBold => Load("BarlowCondensed-ExtraBold");
        public static TMP_FontAsset Bold => Load("BarlowCondensed-Bold");
        public static TMP_FontAsset SemiBold => Load("BarlowCondensed-SemiBold");

        [MenuItem("Puttshack/Pro Shop/Rebuild Fonts", priority = 43)]
        public static void RebuildMenu()
        {
            if (AssetDatabase.IsValidFolder(AssetRoot.TrimEnd('/'))) AssetDatabase.DeleteAsset(AssetRoot.TrimEnd('/'));
            EnsureAll();
            Debug.Log("Pro Shop fonts rebuilt under " + AssetRoot);
        }

        /// <summary>Bakes any missing font assets and material presets. Safe to call on every bake.</summary>
        public static void EnsureAll()
        {
            ProShopBaker.EnsureFolder(AssetRoot.TrimEnd('/'));
            TMP_FontAsset symbols = Ensure("PuttshackDisplay-ExtraBold", Arrows, null);
            var fallback = new[] { symbols };
            Ensure("PuttshackSlabCondensed-Black", Ascii + Punctuation, fallback);
            Ensure("BarlowCondensed-ExtraBold", Ascii + Punctuation, fallback);
            Ensure("BarlowCondensed-Bold", Ascii + Punctuation, fallback);
            Ensure("BarlowCondensed-SemiBold", Ascii + Punctuation, fallback);
            Shadowed(SemiBold);
            AssetDatabase.SaveAssets();
        }

        /// <summary>A soft ink drop shadow for cream text that sits directly over the 3D scene.</summary>
        public static Material Shadowed(TMP_FontAsset font)
        {
            string path = AssetRoot + font.name + " Shadow.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var mat = new Material(font.material) { name = font.name + " Shadow" };
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.04f, 0.06f, 0.06f, 0.85f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.45f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
            ShaderUtilities.UpdateShaderRatios(mat);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static string AssetPath(string ttf) => AssetRoot + ttf + " SDF.asset";

        private static TMP_FontAsset Load(string ttf)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(ttf));
            if (asset == null) throw new InvalidOperationException($"Missing {AssetPath(ttf)}; run Puttshack > Pro Shop > Rebuild Fonts.");
            return asset;
        }

        private static TMP_FontAsset Ensure(string ttf, string characters, TMP_FontAsset[] fallbacks)
        {
            string path = AssetPath(ttf);
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(ProShopImportRules.FontRoot + ttf + ".ttf");
            if (font == null) throw new FileNotFoundException("Pro Shop font source is missing", ProShopImportRules.FontRoot + ttf + ".ttf");

            TMP_FontAsset asset = null;
            string missing = characters;
            foreach (int size in new[] { 1024, 2048 })
            {
                asset = TMP_FontAsset.CreateFontAsset(font, SamplingSize, Padding, GlyphRenderMode.SDFAA, size, size, AtlasPopulationMode.Dynamic, false);
                if (asset == null) throw new InvalidOperationException("TextMeshPro could not load " + font.name);
                asset.TryAddCharacters(characters, out missing, true);
                if (string.IsNullOrEmpty(missing) || !RanOutOfRoom(font, missing)) break;
                UnityEngine.Object.DestroyImmediate(asset.atlasTexture);
                UnityEngine.Object.DestroyImmediate(asset.material);
                UnityEngine.Object.DestroyImmediate(asset);
            }
            if (!string.IsNullOrEmpty(missing)) Debug.Log($"{ttf}: no glyphs for \"{missing}\" (served by fallback fonts).");

            asset.name = ttf + " SDF";
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            if (fallbacks != null) asset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>(fallbacks);

            Texture2D atlas = asset.atlasTexture;
            atlas.name = asset.name + " Atlas";
            Material mat = asset.material;
            mat.name = asset.name + " Material";
            mat.shader = Shader.Find("TextMeshPro/Distance Field");
            mat.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            ShaderUtilities.UpdateShaderRatios(mat);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.AddObjectToAsset(atlas, asset);
            AssetDatabase.AddObjectToAsset(mat, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        /// <summary>True when some of the missing characters exist in the font, i.e. the atlas ran out of room.</summary>
        private static bool RanOutOfRoom(Font font, string missing)
        {
            foreach (char c in missing)
            {
                if (font.HasCharacter(c)) return true;
            }
            return false;
        }
    }
}
