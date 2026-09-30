using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>
    /// Builds the far scenery: concentric rings of ridgeline standing beyond the
    /// playable terrain. Without them the course ends at the skirt and the eye has
    /// nothing to measure distance against, so a 400 m hole reads as a small field.
    /// </summary>
    public static class BackdropBuilder
    {
        private const int SegmentCount = 256;

        /// <summary>
        /// Rings sit far enough apart to overlap and interleave, and are sized so
        /// each crest lands where the reference art puts it: measuring down from the
        /// crest to the horizon gives 9.9, 8.2, 5.8 and 3.2 degrees, which at these
        /// radii is the heights below.
        /// </summary>
        private readonly struct Ridge
        {
            public readonly float Radius;
            public readonly float Height;
            public readonly Color Haze;  // at the base, where the air is thickest
            public readonly Color Crest; // at the top, least atmosphere
            public readonly int Seed;

            public Ridge(float radius, float height, Color haze, Color crest, int seed)
            {
                Radius = radius;
                Height = height;
                Haze = haze;
                Crest = crest;
                Seed = seed;
            }
        }

        /// <summary>
        /// Colours are the reference art's own pixels. Its horizon runs a clean
        /// aerial-perspective ladder, blue over green falling 1.30 in the sky, 1.10
        /// across the ridges, 0.79 at the village and 0.49 in the near woods, so the
        /// far layers are nearly sky-coloured and only the last carries any green.
        /// </summary>
        private static readonly Ridge[] Ridges =
        {
            new Ridge(1750f, 305f, Srgb(0.445f, 0.650f, 0.775f), Srgb(0.380f, 0.561f, 0.616f), 1),
            new Ridge(1450f, 210f, Srgb(0.395f, 0.585f, 0.672f), Srgb(0.333f, 0.502f, 0.569f), 2),
            new Ridge(1150f, 118f, Srgb(0.352f, 0.530f, 0.585f), Srgb(0.318f, 0.486f, 0.537f), 3),
            new Ridge(900f, 55f, Srgb(0.330f, 0.490f, 0.505f), Srgb(0.300f, 0.450f, 0.400f), 4),
        };

        /// <summary>
        /// Drops far below the horizon so no sliver of empty sky shows between the
        /// terrain skirt and the ridge base. A ray leaving the camera even a fifth of
        /// a degree down meets the terrain inside its own radius, so this only has to
        /// cover the near-horizontal band, but elevated tees want the headroom.
        /// </summary>
        private const float BaseDrop = -140f;

        public static GameObject Build(Transform parent, Camera followCamera)
        {
            var root = new GameObject("Backdrop");
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            Shader shader = Shader.Find("RealGolf/BackdropUnlit");
            if (shader == null)
            {
                Debug.LogWarning("BackdropBuilder: RealGolf/BackdropUnlit not found; skipping far scenery.");
                return root;
            }

            for (int i = 0; i < Ridges.Length; i++)
            {
                Ridge ridge = Ridges[i];

                var go = new GameObject($"Ridge{i}");
                go.transform.SetParent(root.transform, false);
                go.isStatic = true;

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = BuildRing(ridge);

                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                var material = new Material(shader)
                {
                    name = $"BackdropRidge{i}",
                    hideFlags = HideFlags.DontSave,
                };
                material.SetColor("_HazeColor", ridge.Haze);
                material.SetColor("_RidgeColor", ridge.Crest);
                material.SetFloat("_GradientPower", 0.75f);
                renderer.sharedMaterial = material;
            }

            var follow = root.AddComponent<BackdropFollow>();
            follow.Target = followCamera != null ? followCamera.transform : null;

            return root;
        }

        private static Mesh BuildRing(Ridge ridge)
        {
            int verts = (SegmentCount + 1) * 2;
            var positions = new Vector3[verts];
            var uvs = new Vector2[verts];
            var triangles = new int[SegmentCount * 6];

            for (int s = 0; s <= SegmentCount; s++)
            {
                float u = (float)s / SegmentCount;
                float angle = u * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * ridge.Radius;
                float z = Mathf.Sin(angle) * ridge.Radius;
                float top = ridge.Height * RidgeProfile(angle, ridge.Seed);

                int b = s * 2;
                positions[b] = new Vector3(x, BaseDrop, z);
                positions[b + 1] = new Vector3(x, top, z);
                uvs[b] = new Vector2(u, 0f);
                uvs[b + 1] = new Vector2(u, 1f);
            }

            for (int s = 0; s < SegmentCount; s++)
            {
                int b = s * 2;
                int t = s * 6;
                triangles[t] = b;
                triangles[t + 1] = b + 1;
                triangles[t + 2] = b + 2;
                triangles[t + 3] = b + 2;
                triangles[t + 4] = b + 1;
                triangles[t + 5] = b + 3;
            }

            var mesh = new Mesh { name = $"BackdropRidge_{ridge.Seed}" };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// Stacked odd harmonics, so no peak spacing repeats around the ring and the
        /// silhouette never betrays that it is a circle. The floor keeps a ridge from
        /// pinching to nothing and tearing a hole in the horizon.
        /// </summary>
        private static float RidgeProfile(float angle, int seed)
        {
            float o = seed * 7.31f;
            float v = 0.55f
                      + 0.24f * Mathf.Sin(angle * 3f + o)
                      + 0.14f * Mathf.Sin(angle * 7f + o * 1.7f)
                      + 0.08f * Mathf.Sin(angle * 13f + o * 2.3f)
                      + 0.05f * Mathf.Sin(angle * 23f + o * 3.1f);
            return Mathf.Clamp(v, 0.18f, 1f);
        }

        /// <summary>
        /// Measurements are display-space sRGB, and that is exactly what these want:
        /// SetColor on a shader property declared as Color already applies the
        /// gamma-to-linear step in a linear project. Converting here as well cost a
        /// second conversion and rendered the ridges at 0.035 luminance against the
        /// 0.22 they were aimed at, near enough to black.
        /// </summary>
        private static Color Srgb(float r, float g, float b)
        {
            return new Color(r, g, b, 1f);
        }
    }

    /// <summary>
    /// Keeps the rings centred on the viewer horizontally so they read as infinitely
    /// far off, while holding their height fixed to the world so the crests do not
    /// bob as the camera rises and falls.
    /// </summary>
    public sealed class BackdropFollow : MonoBehaviour
    {
        public Transform Target;

        private void LateUpdate()
        {
            if (Target == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    return;
                }

                Target = main.transform;
            }

            Vector3 p = Target.position;
            transform.position = new Vector3(p.x, 0f, p.z);
        }
    }
}
