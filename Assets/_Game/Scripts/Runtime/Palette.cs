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

        private static readonly Color[] Players =
        {
            Hex(0x2F6FD6), // Caleb - blue
            Hex(0xE8872A), // Maya - orange
            Hex(0x3E9B4F), // Jordan - green
            Hex(0xD9B021), // Sam - gold
            Hex(0x8A4FC7), // Nia - purple
            Hex(0x1FA59A), // Leo - teal
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
