using UnityEngine;

namespace Puttshack.Runtime.Hud
{
    /// <summary>
    /// Puttshack 2026 brand tokens used by the HUD (design system colors_and_type.css). Panels are Midnight, brand color is
    /// only ever a fill, and type is always White or Midnight: <see cref="TextOn"/> picks the one that reads on a fill.
    /// </summary>
    public static class HudBrand
    {
        public static readonly Color Midnight = Palette.Hex(0x0E0115);
        public static readonly Color White = Color.white;
        public static readonly Color Sun = Palette.Hex(0xFFC829);
        public static readonly Color PinkHot = Palette.Hex(0xE969B0);
        public static readonly Color Pink = Palette.Hex(0xF45197);
        public static readonly Color Plum = Palette.Hex(0x93358D);
        public static readonly Color Plum4 = Palette.Hex(0x863399);
        public static readonly Color Plum5 = Palette.Hex(0x592C82);
        public static readonly Color Aqua = Palette.Hex(0x00C1DE);
        public static readonly Color Blue2 = Palette.Hex(0x0091B3);
        public static readonly Color Navy = Palette.Hex(0x194F90);
        public static readonly Color Blue4 = Palette.Hex(0x1A428A);
        public static readonly Color Blue5 = Palette.Hex(0x1C355E);

        /// <summary>White at 72%: secondary labels on Midnight.</summary>
        public static readonly Color Muted = White.WithAlpha(0.72f);
        /// <summary>White at 14%: rules and dividers on Midnight.</summary>
        public static readonly Color Line = White.WithAlpha(0.14f);
        public static readonly Color Danger = Palette.Hex(0xE64B38);

        public static readonly Color[] RingsPink = { Pink, PinkHot, Plum, Plum4, Plum5 };
        public static readonly Color[] RingsBlue = { Aqua, Blue2, Navy, Blue4, Blue5 };

        public static Color Player(int colorIndex) => Palette.Player(colorIndex);

        /// <summary>Midnight on light fills, White on dark ones.</summary>
        public static Color TextOn(Color fill)
        {
            float luminance = 0.2126f * fill.linear.r + 0.7152f * fill.linear.g + 0.0722f * fill.linear.b;
            return luminance > 0.18f ? Midnight : White;
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
