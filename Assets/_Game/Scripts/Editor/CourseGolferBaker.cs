using System;
using System.IO;
using System.Linq;
using Puttshack.Content;
using Puttshack.Runtime.Shop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Places the on-course golfer in Main.unity: the baked Caleb_Golfer prefab plus a bag stand holding every bag model,
    /// so the shooter walks onto the tee in the outfit and bag saved in the Pro Shop. Runs after the Pro Shop bake
    /// (it reuses the shop's golfer and bag prefabs) and from Build Main Scene.
    /// </summary>
    public static class CourseGolferBaker
    {
        public const string ObjectName = "Course Golfer";

        [MenuItem("Puttshack/Pro Shop/Place Course Golfer", priority = 44)]
        public static void PlaceMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PlaceInMainScene();
        }

        public static void BatchPlace()
        {
            PlaceInMainScene();
            Debug.Log("[Puttshack] Course golfer placed in " + CourseBaker.ScenePath);
        }

        public static void PlaceInMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(CourseBaker.ScenePath, OpenSceneMode.Single);
            PlaceInScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {CourseBaker.ScenePath}.");
        }

        public static GameObject PlaceInScene(Scene scene)
        {
            var caleb = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopBaker.CalebPrefabPath);
            if (caleb == null) throw new InvalidOperationException($"Missing {ProShopBaker.CalebPrefabPath}; run Puttshack > Pro Shop > Bake Pro Shop first.");
            var art = AssetDatabase.LoadAssetAtPath<CosmeticArt>(ProShopBaker.ArtAssetPath);

            foreach (GameObject old in scene.GetRootGameObjects().Where(g => g.GetComponent<CourseGolfer>() != null).ToList()) Object.DestroyImmediate(old);

            var root = new GameObject(ObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var golfer = root.AddComponent<CourseGolfer>();

            var model = (GameObject)PrefabUtility.InstantiatePrefab(caleb, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            golfer.Appearance = model.GetComponent<GolferAppearance>();

            var stand = new GameObject("Bag Stand");
            stand.transform.SetParent(root.transform, false);
            var display = stand.AddComponent<BagDisplay>();
            display.Art = art;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ProShopBaker.PrefabDir + "/Bags" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<BagAppearance>() == null) continue;
                var bag = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stand.transform);
                bag.transform.localPosition = Vector3.zero;
                bag.transform.localRotation = Quaternion.identity;
                display.Styles.Add(bag.GetComponent<BagAppearance>());
            }
            display.ShowBag(CosmeticCatalog.DefaultFor(0).Bag);
            golfer.Bag = display;

            root.SetActive(false);
            return root;
        }
    }
}
