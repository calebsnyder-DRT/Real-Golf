using System.Collections.Generic;
using UnityEngine;

namespace RealGolf.UI
{
    /// <summary>
    /// Loads the Kenney UI art and gives the handful of sprites the menu uses a
    /// name that says what they are for, so the layout code is not littered with
    /// strings like "grey_button_rectangle_depth_flat".
    ///
    /// Everything comes from Resources because the UI is built entirely in code:
    /// there is no prefab and no serialised field anywhere in RealGolf.UI to drag a
    /// sprite into, so Resources.Load is the only route from the asset to a running
    /// Image. Missing art returns null rather than throwing, and every caller is
    /// expected to fall back to a flat colour -- the menu has to stay usable before
    /// the art is imported, which is the state the project was actually in.
    /// </summary>
    public static class UITheme
    {
        private const string SpriteRoot = "UI/Sprites/";
        private const string IconRoot = "UI/Icons/";
        private const string FontRoot = "UI/Fonts/";

        /// <summary>
        /// Resources.Load is a dictionary lookup plus a load on first call, but it is
        /// called per element per rebuild, so the results are held here.
        /// </summary>
        private static readonly Dictionary<string, Sprite> SpriteCache = new();

        private static Font _titleFont;
        private static Font _bodyFont;
        private static bool _titleFontLoaded;
        private static bool _bodyFontLoaded;

        /// <summary>Sprite from Resources/UI/Sprites. Null when not imported yet.</summary>
        public static Sprite Get(string name) => Load(SpriteRoot + name);

        /// <summary>Monochrome glyph from Resources/UI/Icons. Null when not imported yet.</summary>
        public static Sprite Icon(string name) => Load(IconRoot + name);

        private static Sprite Load(string path)
        {
            if (SpriteCache.TryGetValue(path, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = Resources.Load<Sprite>(path);
            SpriteCache[path] = sprite;
            return sprite;
        }

        /// <summary>
        /// Kenney Future, a heavy square display face. Fine for the title and the
        /// big buttons, unreadable in a stat row, which is what BodyFont is for.
        /// </summary>
        public static Font TitleFont
        {
            get
            {
                if (!_titleFontLoaded)
                {
                    _titleFont = Resources.Load<Font>(FontRoot + "Kenney Future");
                    _titleFontLoaded = true;
                }

                return _titleFont != null ? _titleFont : UIFactory.Font;
            }
        }

        /// <summary>
        /// The narrow cut, which keeps the same voice as the title but fits a course
        /// name and its numbers into a card without shrinking to nothing.
        /// </summary>
        public static Font BodyFont
        {
            get
            {
                if (!_bodyFontLoaded)
                {
                    _bodyFont = Resources.Load<Font>(FontRoot + "Kenney Future Narrow");
                    _bodyFontLoaded = true;
                }

                return _bodyFont != null ? _bodyFont : UIFactory.Font;
            }
        }

        // Course cards. The flat variants are used rather than the gloss or gradient
        // ones because a card is a large area: a gradient stretched to card height
        // banks visibly, and the gloss highlight ends up as a bright stripe across
        // the middle of the text.
        public static Sprite CardIdle => Get("grey_button_rectangle_flat");
        public static Sprite CardHover => Get("grey_button_rectangle_border");
        public static Sprite CardSelected => Get("green_button_rectangle_border");

        // The confirm button is the one thing on screen that should look pressable,
        // so it keeps the depth lip and the gloss.
        public static Sprite PlayIdle => Get("green_button_rectangle_depth_gloss");
        public static Sprite PlayHover => Get("green_button_rectangle_depth_gradient");

        /// <summary>
        /// Pressing swaps to the lipless variant, which is what makes the button
        /// look like it travelled down into the page rather than just changing tint.
        /// </summary>
        public static Sprite PlayPressed => Get("green_button_rectangle_flat");

        public static Sprite SmallIdle => Get("grey_button_rectangle_flat");
        public static Sprite SmallSelected => Get("green_button_rectangle_flat");

        public static Sprite Divider => Get("extra_divider");
        public static Sprite StarFilled => Get("green_star");
        public static Sprite StarEmpty => Get("grey_star_outline");

        /// <summary>Dark text, because every Kenney button face is a light tint.</summary>
        public static readonly Color OnButton = new Color(0.16f, 0.18f, 0.16f);

        public static readonly Color Heading = new Color(1f, 0.97f, 0.88f);
        public static readonly Color Body = new Color(0.88f, 0.92f, 0.86f);
        public static readonly Color Muted = new Color(0.66f, 0.71f, 0.66f);

        /// <summary>Scrim over the live sky so the text has something to sit on.</summary>
        public static readonly Color Scrim = new Color(0.02f, 0.05f, 0.03f, 0.55f);
    }
}
