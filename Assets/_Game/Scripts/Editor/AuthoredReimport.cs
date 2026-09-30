using UnityEditor;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>Forces the authored models through the importer again.</summary>
    public static class AuthoredReimport
    {
        private const string Root = "Assets/_Game/Resources/Models/Authored";

        [MenuItem("Real Golf/Reimport Authored Models")]
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { Root });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"REIMPORT {path}");
            }

            AssetDatabase.Refresh();
        }
    }
}
