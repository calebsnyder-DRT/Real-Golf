using System.Linq;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.EditorTools;
using Puttshack.Runtime.Course;
using UnityEditor;
using UnityEngine;

namespace Puttshack.Tests
{
    /// <summary>The course must ship as authored, baked assets that match the definition the solver scores against.</summary>
    public sealed class BakedCourseTests
    {
        [Test]
        public void BakedPrefabs_MatchCourseDefinition()
        {
            CollectionAssert.IsEmpty(CourseBaker.Verify(), "Run Puttshack > Build Main Scene to rebake.");
        }

        [Test]
        public void BakedPrefabs_ReferenceMeshAndMaterialAssets()
        {
            foreach (HoleDefinition hole in GrayboxCourse.Create().Holes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CourseBaker.BakedRoot}/{hole.Id}.prefab");
                Assert.IsNotNull(prefab, hole.Id);
                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    Assert.IsNotNull(filter.sharedMesh, filter.name);
                    Assert.IsTrue(EditorUtility.IsPersistent(filter.sharedMesh), $"{filter.name} mesh is not an asset.");
                }
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in renderer.sharedMaterials)
                    {
                        Assert.IsNotNull(m, $"{renderer.name} has a missing material.");
                        Assert.IsTrue(EditorUtility.IsPersistent(m), $"{renderer.name} material is not an asset.");
                    }
                }
                Assert.IsNotNull(prefab.GetComponent<BakedHole>().Tee);
                Assert.IsNotNull(prefab.GetComponent<BakedHole>().Cup);
                Assert.IsFalse(prefab.GetComponentsInChildren<Collider>(true).Any(), "Visual colliders must not exist; scoring uses the analytic world.");
            }
        }

        [Test]
        public void MainScene_IsTheOnlyBuildScene()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.AreEqual(1, scenes.Length);
            Assert.AreEqual(CourseBaker.ScenePath, scenes[0].path);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(CourseBaker.ScenePath));
        }
    }
}
