using System.IO;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>Fonts and sprites used by the HUD builder.</summary>
    public static partial class HudUIBuilder
    {
        private static TMP_FontAsset _bold, _book, _twist, _condensed, _condensedBold, _wide;
        private static Sprite _round, _lineR10, _linePill, _lineR28, _lineR24, _ringThin, _ringMid, _ringBand, _arrow, _fadeH, _wordmark, _logoPin;
        private static Sprite[] _scoreMarks;

        /// <summary>Brand fonts and flat sprites for other builders (the Pro Shop UI); call <see cref="LoadBrandArt"/> first.</summary>
        internal static TMP_FontAsset BoldFont => _bold;
        internal static TMP_FontAsset BookFont => _book;
        internal static TMP_FontAsset CondensedFont => _condensed;
        internal static TMP_FontAsset CondensedBoldFont => _condensedBold;
        internal static TMP_FontAsset WideFont => _wide;
        internal static Sprite RoundSprite => _round;
        internal static Sprite LinePill => _linePill;
        internal static Sprite LineR10 => _lineR10;
        internal static Sprite LineR24 => _lineR24;
        internal static Sprite Wordmark => _wordmark;

        internal static void LoadBrandArt()
        {
            HudBaker.ConfigureSprites();
            LoadArt();
        }

        private static void AssignArt(HudView view)
        {
            view.MarkBirdie = _scoreMarks[0];
            view.MarkEagle = _scoreMarks[1];
            view.MarkBogey = _scoreMarks[2];
            view.MarkDouble = _scoreMarks[3];
        }

        private static void LoadArt()
        {
            HudFonts.EnsureAll();
            _bold = HudFonts.Bold;
            _book = HudFonts.Book;
            _twist = HudFonts.Twist;
            _condensed = HudFonts.Condensed;
            _condensedBold = HudFonts.CondensedBold;
            _wide = HudFonts.Wide;

            _round = Hud("flat_round");
            _lineR10 = Hud("flat_line_r10");
            _linePill = Hud("flat_line_pill");
            _lineR28 = Hud("flat_line_r28");
            _lineR24 = Hud("flat_line_r24");
            _ringThin = Hud("flat_ring_thin");
            _ringMid = Hud("flat_ring_mid");
            _ringBand = Hud("flat_ring_band");
            _arrow = Hud("flat_arrow");
            _fadeH = Hud("flat_fade_h");
            _wordmark = Hud("flat_wordmark");
            _logoPin = Hud("flat_logo_pin");

            _scoreMarks = new[] { Hud("score_circle"), Hud("score_circle2"), Hud("score_square"), Hud("score_square2") };
        }

        private static Sprite Hud(string name)
        {
            string path = HudBaker.SpriteRoot + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                throw new FileNotFoundException("Missing HUD sprite; run ArtSource/UI/HUD/make_hud_flat.py (flat_*) or make_hud_ui.py, then Puttshack > HUD > Bake HUD.", path);
            return sprite;
        }

    }
}
