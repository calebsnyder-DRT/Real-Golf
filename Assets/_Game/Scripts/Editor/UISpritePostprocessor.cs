using UnityEditor;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// Import rules for the Kenney UI art under Resources/UI. Without these the PNGs
    /// land as default 2D textures, which cannot be assigned to an Image at all, so
    /// the menu would have nothing to draw.
    ///
    /// The art sits under Resources because the whole UI is built from code --
    /// UIFactory and CourseSelectMenu have no serialised fields to drag a sprite
    /// into -- so Resources.Load is the only way a sprite reaches a running Image.
    /// The model postprocessors are all scoped to Resources/Models/, so sharing the
    /// Resources tree with them creates no ordering conflict.
    /// </summary>
    public sealed class UISpritePostprocessor : AssetPostprocessor
    {
        private const string UIRoot = "Assets/_Game/Resources/UI/";

        /// <summary>
        /// Kenney draws the rounded rectangle chrome at 192x64 with a 16 px corner
        /// radius, so 16 is the largest border that never clips a corner into the
        /// stretched middle. Square and round variants are 64x64 and share the radius.
        /// </summary>
        private const int CornerRadius = 16;

        /// <summary>
        /// The "depth" variants carry an extra lip along the bottom edge that reads as
        /// the button's thickness. Slicing it at the plain corner radius smears that
        /// lip when the button is stretched vertically, so the bottom gets more.
        /// </summary>
        private const int DepthLip = 24;

        /// <summary>Slider tracks are 96x16 with rounded caps about 8 px wide.</summary>
        private const int TrackCap = 8;

        public override uint GetVersion() => 1;

        private bool IsUIAsset => assetPath.Replace('\\', '/').StartsWith(UIRoot);

        private void OnPreprocessTexture()
        {
            if (!IsUIAsset)
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;

            // The canvas draws these at a fixed reference resolution, so mips only
            // ever soften them, and block compression bands the gradient variants.
            // The whole set is under 200 sprites of <=192 px, so leaving it
            // uncompressed costs very little memory.
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;

            importer.spriteBorder = BorderFor(System.IO.Path.GetFileNameWithoutExtension(assetPath));
        }

        /// <summary>
        /// Nine-slice border in pixels, ordered (left, bottom, right, top). Anything
        /// that is drawn at its authored size -- icons, stars, checkmarks, slider
        /// handles, bar caps -- gets a zero border so Unity leaves it alone.
        /// </summary>
        private static Vector4 BorderFor(string name)
        {
            string n = name.ToLowerInvariant();

            // Stretchable chrome: buttons and text fields.
            bool isButton = n.Contains("button_rectangle") || n.Contains("button_square");
            bool isInput = n.Contains("input_rectangle") || n.Contains("input_square");
            if (isButton || isInput)
            {
                float bottom = n.Contains("depth") ? DepthLip : CornerRadius;
                return new Vector4(CornerRadius, bottom, CornerRadius, CornerRadius);
            }

            // Slider tracks stretch along their long axis only.
            if (n.Contains("slide_horizontal"))
            {
                return new Vector4(TrackCap, 0f, TrackCap, 0f);
            }

            if (n.Contains("slide_vertical"))
            {
                return new Vector4(0f, TrackCap, 0f, TrackCap);
            }

            // Round buttons must not be sliced: stretching a circle's middle turns it
            // into a capsule, which is never what a round button wants.
            return Vector4.zero;
        }
    }
}
