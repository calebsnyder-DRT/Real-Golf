using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// ProShop.unity is a baked look-dev scene with no GameHost, so pressing Play there launches Main instead, which
    /// opens in the Pro Shop. Any other scene (including the test runner's) plays as itself.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromMain
    {
        static PlayFromMain()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (_, __) => Refresh();
            EditorSceneManager.sceneOpened += (_, __) => Refresh();
            EditorSceneManager.newSceneCreated += (_, __, ___) => Refresh();
            EditorApplication.delayCall += Refresh;
        }

        private static void Refresh()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var main = AssetDatabase.LoadAssetAtPath<SceneAsset>(CourseBaker.ScenePath);
            if (SceneManager.GetActiveScene().path == ProShopBaker.PreviewScenePath)
                EditorSceneManager.playModeStartScene = main;
            else if (EditorSceneManager.playModeStartScene == main)
                EditorSceneManager.playModeStartScene = null;
        }
    }
}
