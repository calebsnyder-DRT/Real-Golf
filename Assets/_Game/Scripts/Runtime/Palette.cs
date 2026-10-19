using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime
{
    /// <summary>Spec section 30 tokens plus player identity colors. Color is always paired with a name or label.</summary>
    public static class Palette
    {
        public static readonly Color Cream = Hex(0xF2EDDF);
        public static readonly Color Ink = Hex(0x192020);
        public static readonly Color Cyan = Hex(0x00CBE7);
        public static readonly Color CyanOnLight = Hex(0x008CA1);
        public static readonly Color MutedGreenGray = Hex(0x536260);
        public static readonly Color PaleBorder = Hex(0xC9C3B5);
        public static readonly Color Warning = Hex(0xD9822B);
        public static readonly Color Danger = Hex(0xC2412D);

        /// <summary>Player identities from the 2026 brand palette (cool blue, vivid magenta and sun ramps).</summary>
        private static readonly Color[] Players =
        {
            Hex(0x00C1DE), // Caleb - aqua
            Hex(0xF45197), // Maya - pink
            Hex(0xFFC829), // Jordan - sun
            Hex(0xE969B0), // Sam - hot pink
            Hex(0x0091B3), // Nia - blue
            Hex(0x863399), // Leo - plum
        };

        public static Color Player(int colorIndex) => Players[((colorIndex % Players.Length) + Players.Length) % Players.Length];

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    public static class VecExtensions
    {
        public static Vector3 ToUnity(this Vec3d v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
        public static Vec3d ToDomain(this Vector3 v) => new Vec3d(v.x, v.y, v.z);
    }
}
