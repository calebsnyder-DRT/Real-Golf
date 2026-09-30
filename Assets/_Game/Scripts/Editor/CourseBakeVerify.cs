using RealGolf.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>Batch check on the editor course bake and the runtime alignment.</summary>
    public static class CourseBakeVerify
    {
        private const string ScenePath = "Assets/_Game/Scenes/RealGolf.unity";

        public static void Run()
        {
            Debug.Log("BAKE-BEGIN");
            EditorSceneManager.OpenScene(ScenePath);
            CourseBaker.Bake();

            var baked = Object.FindFirstObjectByType<BakedCourse>();
            if (baked == null)
            {
                Debug.Log("BAKE FAIL no BakedCourse in scene");
                return;
            }

            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool init = false;
            int missing = 0, renderers = 0;
            foreach (Transform t in baked.HoleRoots)
            {
                if (t == null) { missing++; continue; }
                foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>())
                {
                    renderers++;
                    if (!init) { bounds = r.bounds; init = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            Debug.Log($"BAKE holes={baked.HoleCount} missing={missing} renderers={renderers} " +
                      $"courseExtent=({bounds.size.x:F0} x {bounds.size.z:F0} m)");

            // Alignment: after AlignTo(i) the hole must sit at the world origin with
            // no yaw, because that is where its analytic terrain lives.
            foreach (int i in new[] { 0, 1, 7, 17 })
            {
                if (i >= baked.HoleCount) continue;
                baked.AlignTo(i);
                Transform hole = baked.Hole(i);
                float offset = hole.position.magnitude;
                float yaw = Quaternion.Angle(hole.rotation, Quaternion.identity);
                Debug.Log($"BAKE alignTo({i}): holeWorldPos={offset:F4}m yawError={yaw:F4}deg " +
                          $"{(offset < 0.01f && yaw < 0.01f ? "OK" : "MISALIGNED")}");
            }

            baked.AlignTo(0);
            string bundle = $"Assets/_Game/Art/Course/BakedCourse_{baked.Seed}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CourseAssetBundle>(bundle);
            Object[] subs = AssetDatabase.LoadAllAssetsAtPath(bundle);
            Debug.Log($"BAKE bundle={(asset != null ? "found" : "MISSING")} " +
                      $"meshes={(asset != null ? asset.MeshCount : 0)} " +
                      $"materials={(asset != null ? asset.MaterialCount : 0)} subObjects={subs.Length}");
            Debug.Log("BAKE-END");
        }
    }
}
