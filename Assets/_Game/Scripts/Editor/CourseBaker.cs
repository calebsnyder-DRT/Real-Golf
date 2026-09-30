using System.Collections.Generic;
using RealGolf.Core;
using RealGolf.Course;
using RealGolf.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// Generates the whole course in the editor and saves it into the open scene.
    ///
    /// The game used to build one hole on Play and destroy it when you moved on,
    /// which meant nothing but the hole you stood on ever existed: the middle
    /// distance was empty and you could never see the rest of the course. Baking
    /// puts all eighteen holes in the scene at their routed positions, so the course
    /// is there to look at and there is no build cost on Play.
    /// </summary>
    public static class CourseBaker
    {
        private const string BundleDirectory = "Assets/_Game/Art/Course";
        private const string CourseRootName = "Course (Baked)";

        [MenuItem("Real Golf/Bake Course Into Scene", false, 10)]
        public static void Bake() => Bake(0);

        /// <summary>
        /// Eighteen holes at this mesh density is a lot to hold in one scene, so the
        /// front nine is offered separately for a lighter scene while iterating.
        /// </summary>
        [MenuItem("Real Golf/Bake Front Nine Into Scene", false, 11)]
        public static void BakeFrontNine() => Bake(9);

        /// <param name="holeLimit">0 bakes every hole.</param>
        public static void Bake(int holeLimit)
        {
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            int seed = bootstrap != null && !bootstrap.RandomiseSeed ? bootstrap.Seed : 12345;
            if (bootstrap != null && bootstrap.RandomiseSeed)
            {
                Debug.LogWarning($"[CourseBaker] {nameof(GameBootstrap)}.RandomiseSeed is on, so the " +
                                 $"scene had no fixed layout to bake. Using seed {seed}; the bake pins " +
                                 "the seed and the game will follow it.");
            }

            Clear(false);

            CourseDefinition course = CourseGenerator.Generate(seed);
            List<HolePose> poses = CourseRouting.Layout(course);
            int holeCount = holeLimit > 0 ? Mathf.Min(holeLimit, course.Holes.Count) : course.Holes.Count;

            var rootGo = new GameObject(CourseRootName);
            var baked = rootGo.AddComponent<BakedCourse>();
            baked.Seed = seed;

            var roots = new Transform[holeCount];
            try
            {
                for (int i = 0; i < holeCount; i++)
                {
                    HoleDefinition definition = course.Holes[i];
                    EditorUtility.DisplayProgressBar("Baking course",
                        $"Hole {definition.Number} of {holeCount}",
                        (float)i / holeCount);

                    BuiltHole built = HoleBuilder.Build(definition);
                    Transform t = built.Root.transform;
                    t.SetParent(rootGo.transform, false);
                    t.localPosition = poses[i].Position;
                    t.localRotation = poses[i].Rotation;
                    roots[i] = t;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            baked.HoleRoots = roots;
            for (int i = 0; i < roots.Length; i++)
            {
                baked.StorePose(i, poses[i].Position, poses[i].Yaw);
            }

            int meshes, materials;
            PersistGeneratedAssets(rootGo, seed, out meshes, out materials);

            // Deliberately not marked static: the course root slides at runtime so the
            // hole in play lands on its own analytic terrain, and static batching
            // assumes geometry never moves.

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            int shaderless = CountShaderlessMaterials(rootGo);
            Debug.Log($"[CourseBaker] Baked {roots.Length} holes from seed {seed}: " +
                      $"{meshes} meshes and {materials} materials saved as sub-assets" +
                      (shaderless > 0
                          ? $". WARNING: {shaderless} materials have no shader -- bake from the " +
                            "editor GUI rather than batch mode, where Shader.Find returns null."
                          : "."));
        }

        [MenuItem("Real Golf/Clear Baked Course", false, 12)]
        public static void ClearMenu() => Clear(true);

        private static void Clear(bool save)
        {
            foreach (BakedCourse existing in Object.FindObjectsByType<BakedCourse>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            if (save)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("[CourseBaker] Cleared the baked course; the game will fall back to " +
                          "generating a hole at a time on Play.");
            }
        }

        /// <summary>
        /// Moves every runtime-created mesh and material under the course into one
        /// asset file, then leaves the scene pointing at those sub-assets.
        /// </summary>
        private static void PersistGeneratedAssets(GameObject root, int seed, out int meshCount, out int materialCount)
        {
            if (!AssetDatabase.IsValidFolder(BundleDirectory))
            {
                System.IO.Directory.CreateDirectory(BundleDirectory);
                AssetDatabase.Refresh();
            }

            string path = $"{BundleDirectory}/BakedCourse_{seed}.asset";
            AssetDatabase.DeleteAsset(path);

            var bundle = ScriptableObject.CreateInstance<CourseAssetBundle>();
            bundle.Seed = seed;
            AssetDatabase.CreateAsset(bundle, path);

            var seenMeshes = new HashSet<Mesh>();
            var seenMaterials = new HashSet<Material>();

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || AssetDatabase.Contains(mesh) || !seenMeshes.Add(mesh))
                {
                    continue;
                }

                AssetDatabase.AddObjectToAsset(mesh, bundle);
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || AssetDatabase.Contains(material) || !seenMaterials.Add(material))
                    {
                        continue;
                    }

                    AssetDatabase.AddObjectToAsset(material, bundle);
                }
            }

            meshCount = seenMeshes.Count;
            materialCount = seenMaterials.Count;
            bundle.MeshCount = meshCount;
            bundle.MaterialCount = materialCount;

            EditorUtility.SetDirty(bundle);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
        }

        private static int CountShaderlessMaterials(GameObject root)
        {
            var seen = new HashSet<Material>();
            int shaderless = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && seen.Add(material) && material.shader == null)
                    {
                        shaderless++;
                    }
                }
            }

            return shaderless;
        }
    }
}
