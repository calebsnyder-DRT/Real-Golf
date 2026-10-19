using TMPro;
using UnityEditor;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Static SDF font assets for the brand HUD, baked from the Ringside TTFs in <see cref="FontRoot"/> (converted from the
    /// Puttshack design system's web fonts). Numerics use Ringside Wide, labels Ringside Condensed, headlines and buttons
    /// Ringside Bold. The "twist" celebration words call for Gyst Bold Italic, which the design system has not shipped yet,
    /// so Ringside Bold Italic stands in. <c>Puttshack > HUD > Rebuild Fonts</c> regenerates them.
    /// </summary>
    public static class HudFonts
    {
        public const string FontRoot = "Assets/_Game/Art/HUD/Fonts/";
        public const string AssetRoot = FontRoot + "SDF/";

        private const string Extra = "− ";
        private const int SamplingSize = 90;
        private const int Padding = 10;

        public static TMP_FontAsset Bold => Load("Ringside-Regular-Bold");
        public static TMP_FontAsset Book => Load("Ringside-Regular-Book");
        public static TMP_FontAsset Twist => Load("Ringside-Regular-BoldItal");
        public static TMP_FontAsset Condensed => Load("Ringside-Condensed-Semibold");
        public static TMP_FontAsset CondensedBold => Load("Ringside-Condensed-Bold");
        public static TMP_FontAsset Wide => Load("Ringside-Wide-Ultra");

        private static readonly string[] Faces =
        {
            "Ringside-Regular-Bold", "Ringside-Regular-Book", "Ringside-Regular-BoldItal",
            "Ringside-Condensed-Semibold", "Ringside-Condensed-Bold", "Ringside-Wide-Ultra",
        };

        [MenuItem("Puttshack/HUD/Rebuild Fonts", priority = 62)]
        public static void RebuildMenu()
        {
            if (AssetDatabase.IsValidFolder(AssetRoot.TrimEnd('/'))) AssetDatabase.DeleteAsset(AssetRoot.TrimEnd('/'));
            EnsureAll();
            Debug.Log("HUD fonts rebuilt under " + AssetRoot);
        }

        /// <summary>Bakes any missing font assets. Safe to call on every bake.</summary>
        public static void EnsureAll()
        {
            ProShopFonts.EnsureAll();
            ProShopBaker.EnsureFolder(AssetRoot.TrimEnd('/'));
            var fallback = new[] { ProShopFonts.Symbols };
            foreach (string face in Faces)
                ProShopFonts.Bake(FontRoot + face + ".ttf", AssetPath(face), ProShopFonts.Ascii + ProShopFonts.Punctuation + Extra, fallback, SamplingSize, Padding);
            AssetDatabase.SaveAssets();
        }

        private static string AssetPath(string face) => AssetRoot + face + " SDF.asset";

        private static TMP_FontAsset Load(string face)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(face));
            if (asset == null) throw new System.InvalidOperationException($"Missing {AssetPath(face)}; run Puttshack > HUD > Rebuild Fonts.");
            return asset;
        }
    }
}
