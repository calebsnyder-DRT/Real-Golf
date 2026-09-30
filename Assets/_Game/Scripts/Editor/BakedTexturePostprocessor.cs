using UnityEditor;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// Import rules for the maps baked out of Blender. Normal maps have to be
    /// tagged as such, and the albedo must keep its alpha untouched because URP
    /// reads smoothness from that channel rather than a separate roughness map.
    /// </summary>
    public sealed class BakedTexturePostprocessor : AssetPostprocessor
    {
        private const string TextureRoot = "Assets/_Game/Resources/Models/";

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(TextureRoot))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;

            if (path.EndsWith("_normal.png"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                return;
            }

            if (path.EndsWith("_albedo.png"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }
        }
    }
}
