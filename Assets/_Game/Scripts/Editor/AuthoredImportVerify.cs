using RealGolf.Core;
using UnityEditor;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>Batch-mode check that the authored Blender models import as the game expects.</summary>
    public static class AuthoredImportVerify
    {
        private static readonly string[] Paths =
        {
            "Models/Authored/Trees/tree_broad",
            "Models/Authored/Trees/tree_slim",
            "Models/Authored/Props/bush_mound",
            "Models/Authored/Props/plant_flowering",
            "Models/Authored/Props/rock_cluster",
            "Models/Authored/Structures/clubhouse",
            "Models/Authored/Structures/bridge_stone",
            "Models/Authored/Golf/hole_complex",
            // Kit models too: the authored postprocessor had to exclude them from the
            // Kenney one, so this checks that change did not regress the kit.
            "Models/Trees/tree_oak",
            "Models/Props/rock_smallA",
        };

        public static void Run()
        {
            Debug.Log("VERIFY-BEGIN");
            foreach (string path in Paths)
            {
                var prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    Debug.Log($"VERIFY MISSING {path}");
                    continue;
                }

                var filters = prefab.GetComponentsInChildren<MeshFilter>();
                int tris = 0;
                foreach (MeshFilter f in filters)
                {
                    if (f.sharedMesh != null) tris += f.sharedMesh.triangles.Length / 3;
                }

                // Instantiate so the FBX node transforms (which carry Blender's
                // Z-up -> Y-up conversion) are applied; mesh-local bounds would lie.
                var probe = Object.Instantiate(prefab);
                probe.transform.position = Vector3.zero;
                probe.transform.rotation = Quaternion.identity;
                probe.transform.localScale = Vector3.one;
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool init = false;
                foreach (MeshRenderer r in probe.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!init) { bounds = r.bounds; init = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                Object.DestroyImmediate(probe);

                var sb = new System.Text.StringBuilder();
                sb.Append($"VERIFY {path} meshes={filters.Length} tris={tris} " +
                          $"size=({bounds.size.x:F2},{bounds.size.y:F2},{bounds.size.z:F2}) mats=[");
                foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>())
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) { sb.Append("NULL "); continue; }
                        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.magenta;
                        sb.Append($"{m.name}:{m.shader.name.Replace("Universal Render Pipeline/", "URP/")}" +
                                  $"({c.r:F2},{c.g:F2},{c.b:F2}) ");
                    }
                }
                sb.Append(']');
                Debug.Log(sb.ToString());
            }

            Debug.Log($"VERIFY ModelLibrary.Trees={ModelLibrary.Trees.Count} " +
                      $"Props={ModelLibrary.Props.Count} " +
                      $"Clubhouse={(ModelLibrary.Clubhouse != null)} " +
                      $"Bridge={(ModelLibrary.StoneBridge != null)} " +
                      $"HoleComplex={(ModelLibrary.HoleComplex != null)} " +
                      $"BakedFlagstick={(ModelLibrary.BakedFlagstick != null ? ModelLibrary.BakedFlagstick.name : "null")}");
            Debug.Log("VERIFY-END");
        }
    }
}
