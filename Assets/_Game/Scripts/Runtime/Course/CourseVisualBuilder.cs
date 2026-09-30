using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Builds a hole's visuals from the same <see cref="HoleDefinition"/> and analytic terrain the solver uses.
    /// The editor baker calls this once and saves the result as assets; the runtime only calls it as a fallback.
    /// </summary>
    public static class CourseVisualBuilder
    {
        public const float GroundSpacing = 0.8f;
        public const float GroundMargin = 30f;

        public static BakedHole Build(HoleDefinition hole, CourseVisualAssets assets, Transform parent)
        {
            var world = new AnalyticWorld(hole);
            var root = new GameObject($"Hole {hole.Number:00} - {hole.Name}");
            root.transform.SetParent(parent, false);

            var baked = root.AddComponent<BakedHole>();
            baked.HoleId = hole.Id;
            baked.HoleVersion = hole.Version;
            baked.ContentHash = hole.ContentHash;

            AddMesh(root.transform, "Ground", BuildGroundMesh(hole, world), assets.Ground, receiveShadows: true, castShadows: false);

            var hazardPolygons = new List<List<Vec2d>>();
            foreach (HazardVolume hazard in hole.Hazards) hazardPolygons.Add(hazard.Polygon);
            Mesh water = BuildOverlayMesh($"{hole.Id}_water", hole, world, hazardPolygons, 0.05f, true);
            if (water != null) AddMesh(root.transform, "Water", water, assets.Water, receiveShadows: false, castShadows: false);

            var pathPolygons = new List<List<Vec2d>>();
            foreach (SurfaceRegion region in hole.Surfaces)
            {
                if (region.Surface == SurfaceId.CartPath) pathPolygons.Add(region.Polygon);
            }
            Mesh path = BuildOverlayMesh($"{hole.Id}_cartpath", hole, world, pathPolygons, 0.02f, false);
            if (path != null) AddMesh(root.transform, "Cart Path", path, assets.CartPath, receiveShadows: true, castShadows: false);

            var trees = new GameObject("Trees").transform;
            trees.SetParent(root.transform, false);
            for (int i = 0; i < hole.Trees.Count; i++) BuildTree(hole.Trees[i], i, world, assets, trees);

            baked.Tee = BuildTee(hole, world, assets, root.transform);
            baked.Cup = BuildCupAndFlag(hole, world, assets, root.transform);
            BuildBoundaryStakes(hole, world, assets, root.transform);
            return baked;
        }

        public static Mesh BuildGroundMesh(HoleDefinition hole, ICollisionWorld world)
        {
            GetBounds(hole, GroundMargin, out float minX, out float minZ, out float maxX, out float maxZ);
            int nx = Mathf.CeilToInt((maxX - minX) / GroundSpacing) + 1;
            int nz = Mathf.CeilToInt((maxZ - minZ) / GroundSpacing) + 1;

            var vertices = new Vector3[nx * nz];
            var normals = new Vector3[nx * nz];
            var colors = new Color32[nx * nz];
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    double x = minX + ix * GroundSpacing;
                    double z = minZ + iz * GroundSpacing;
                    int i = iz * nx + ix;
                    vertices[i] = new Vector3((float)x, (float)world.GroundHeight(x, z), (float)z);
                    normals[i] = world.GroundNormal(x, z).ToUnity();
                    colors[i] = SurfaceWeights(world.InBounds(x, z) ? world.SurfaceAt(x, z) : SurfaceId.DeepRough);
                }
            }

            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int iz = 0; iz < nz - 1; iz++)
            {
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    int a = iz * nx + ix;
                    int b = a + 1;
                    int c = a + nx;
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = $"{hole.Id}_ground", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Grid-cell overlay draped on the terrain for cells whose center satisfies <paramref name="inside"/>.</summary>
        /// <summary>
        /// Terrain-draped overlay for the given polygons. Grid cells crossing an edge are clipped to the polygon so the
        /// outline follows the exact gameplay boundary. Edge tint (vertex color R) fades over <see cref="EdgeTintWidth"/>.
        /// </summary>
        public static Mesh BuildOverlayMesh(string name, HoleDefinition hole, ICollisionWorld world, IReadOnlyList<List<Vec2d>> polygons, float lift, bool edgeTint)
        {
            if (polygons == null || polygons.Count == 0) return null;
            float s = GroundSpacing * 0.5f;
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            var cell = new List<Vector2>(8);

            foreach (List<Vec2d> polygon in polygons)
            {
                if (polygon == null || polygon.Count < 3) continue;
                List<Vector2> clip = ToCounterClockwise(polygon);
                bool convex = IsConvex(clip);
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (Vector2 p in clip)
                {
                    minX = Mathf.Min(minX, p.x);
                    minZ = Mathf.Min(minZ, p.y);
                    maxX = Mathf.Max(maxX, p.x);
                    maxZ = Mathf.Max(maxZ, p.y);
                }
                minX = Mathf.Floor(minX / s) * s;
                minZ = Mathf.Floor(minZ / s) * s;
                int nx = Mathf.CeilToInt((maxX - minX) / s);
                int nz = Mathf.CeilToInt((maxZ - minZ) / s);

                for (int iz = 0; iz < nz; iz++)
                {
                    for (int ix = 0; ix < nx; ix++)
                    {
                        float x0 = minX + ix * s, z0 = minZ + iz * s;
                        cell.Clear();
                        cell.Add(new Vector2(x0, z0));
                        cell.Add(new Vector2(x0 + s, z0));
                        cell.Add(new Vector2(x0 + s, z0 + s));
                        cell.Add(new Vector2(x0, z0 + s));

                        List<Vector2> piece;
                        if (convex) piece = ClipConvex(cell, clip);
                        else piece = Shapes.Contains(polygon, x0 + s * 0.5, z0 + s * 0.5) ? new List<Vector2>(cell) : null;
                        if (piece == null || piece.Count < 3) continue;

                        int start = vertices.Count;
                        foreach (Vector2 v in piece)
                        {
                            vertices.Add(new Vector3(v.x, (float)world.GroundHeight(v.x, v.y) + lift, v.y));
                            float shade = edgeTint ? Mathf.Lerp(1f, 0.27f, Mathf.Clamp01(DistanceToEdge(clip, v) / EdgeTintWidth)) : 1f;
                            byte b = (byte)Mathf.RoundToInt(shade * 255);
                            colors.Add(new Color32(b, b, b, 255));
                        }
                        for (int k = 1; k < piece.Count - 1; k++)
                        {
                            triangles.Add(start);
                            triangles.Add(start + k + 1);
                            triangles.Add(start + k);
                        }
                    }
                }
            }
            if (triangles.Count == 0) return null;

            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color32 SurfaceWeights(SurfaceId surface)
        {
            switch (surface)
            {
                case SurfaceId.Tee:
                case SurfaceId.Fairway:
                    return new Color32(255, 0, 0, 0);
                case SurfaceId.Green:
                    return new Color32(0, 255, 0, 0);
                case SurfaceId.Bunker:
                    return new Color32(0, 0, 255, 0);
                case SurfaceId.DeepRough:
                    return new Color32(0, 0, 0, 255);
                default:
                    return new Color32(0, 0, 0, 0);
            }
        }

        private const float EdgeTintWidth = 2.5f;

        private static List<Vector2> ToCounterClockwise(List<Vec2d> polygon)
        {
            var points = new List<Vector2>(polygon.Count);
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vec2d a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.z - b.x * a.z;
                points.Add(new Vector2((float)a.x, (float)a.z));
            }
            if (area < 0) points.Reverse();
            return points;
        }

        private static bool IsConvex(List<Vector2> ccw)
        {
            for (int i = 0; i < ccw.Count; i++)
            {
                Vector2 a = ccw[i], b = ccw[(i + 1) % ccw.Count], c = ccw[(i + 2) % ccw.Count];
                if (Cross(b - a, c - b) < -1e-5f) return false;
            }
            return true;
        }

        /// <summary>Sutherland-Hodgman clip of a polygon against a convex counter-clockwise polygon.</summary>
        private static List<Vector2> ClipConvex(List<Vector2> subject, List<Vector2> clip)
        {
            var output = new List<Vector2>(subject);
            for (int e = 0; e < clip.Count && output.Count > 0; e++)
            {
                Vector2 a = clip[e], b = clip[(e + 1) % clip.Count];
                var input = output;
                output = new List<Vector2>(input.Count + 2);
                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 cur = input[i], prev = input[(i + input.Count - 1) % input.Count];
                    float dc = Cross(b - a, cur - a), dp = Cross(b - a, prev - a);
                    if (dc >= 0)
                    {
                        if (dp < 0) output.Add(Vector2.Lerp(prev, cur, dp / (dp - dc)));
                        output.Add(cur);
                    }
                    else if (dp >= 0)
                    {
                        output.Add(Vector2.Lerp(prev, cur, dp / (dp - dc)));
                    }
                }
            }
            return output;
        }

        private static float DistanceToEdge(List<Vector2> polygon, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static void BuildTree(TreeObstacle tree, int index, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            var basePos = new Vector3((float)tree.Position.x, (float)world.GroundHeight(tree.Position.x, tree.Position.z), (float)tree.Position.z);
            bool pine = tree.Id.StartsWith("pine", StringComparison.Ordinal);
            List<GameObject> models = pine ? assets.PineModels : assets.BroadleafModels;

            if (models.Count > 0)
            {
                GameObject model = models[index % models.Count];
                GameObject instance = InstantiateModel(model, parent);
                instance.name = tree.Id;
                instance.transform.position = basePos;
                instance.transform.rotation = Quaternion.Euler(0, (index * 137.5f) % 360f, 0);
                FitHeight(instance, (float)tree.CanopyHeight);
                return;
            }

            var root = new GameObject(tree.Id).transform;
            root.SetParent(parent, false);
            root.position = basePos;
            GameObject trunk = Primitive(PrimitiveType.Cylinder, "Trunk", root, assets.Trunk, true);
            trunk.transform.localScale = new Vector3((float)tree.TrunkRadius * 2, (float)tree.TrunkHeight * 0.5f, (float)tree.TrunkRadius * 2);
            trunk.transform.localPosition = new Vector3(0, (float)tree.TrunkHeight * 0.5f, 0);
            GameObject canopy = Primitive(PrimitiveType.Sphere, "Canopy", root, assets.Canopy, true);
            float canopyTall = (float)(tree.CanopyHeight - tree.TrunkHeight) + (float)tree.CanopyRadius;
            canopy.transform.localScale = new Vector3((float)tree.CanopyRadius * 2, canopyTall, (float)tree.CanopyRadius * 2);
            canopy.transform.localPosition = new Vector3(0, (float)tree.CanopyHeight - canopyTall * 0.5f, 0);
        }

        private static Transform BuildTee(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            var tee = new GameObject("Tee").transform;
            tee.SetParent(parent, false);
            tee.position = hole.TeeM.ToUnity();
            foreach (float side in new[] { -2.5f, 2.5f })
            {
                GameObject marker = Primitive(PrimitiveType.Sphere, "Tee Marker", tee, assets.TeeMarker, true);
                double x = hole.TeeM.x + side;
                double z = hole.TeeM.z + 1.2;
                marker.transform.position = new Vector3((float)x, (float)world.GroundHeight(x, z) + 0.1f, (float)z);
                marker.transform.localScale = Vector3.one * 0.22f;
            }
            return tee;
        }

        private static Transform BuildCupAndFlag(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            var cup = new GameObject("Cup").transform;
            cup.SetParent(parent, false);
            Vector3 p = hole.CupM.ToUnity();
            p.y = (float)world.GroundHeight(hole.CupM.x, hole.CupM.z);
            cup.position = p;

            GameObject hole3d = Primitive(PrimitiveType.Cylinder, "Cup Liner", cup, assets.Cup, false);
            float d = (float)hole.Cup.CupRadius * 2;
            hole3d.transform.localScale = new Vector3(d, 0.004f, d);
            hole3d.transform.localPosition = new Vector3(0, 0.004f, 0);

            GameObject pole = Primitive(PrimitiveType.Cylinder, "Flagstick", cup, assets.FlagPole, true);
            pole.transform.localScale = new Vector3(0.025f, 1.15f, 0.025f);
            pole.transform.localPosition = new Vector3(0, 1.15f, 0);

            GameObject cloth = Primitive(PrimitiveType.Cube, "Flag", cup, assets.FlagCloth, true);
            cloth.transform.localScale = new Vector3(0.55f, 0.36f, 0.01f);
            cloth.transform.localPosition = new Vector3(0.29f, 2.1f, 0);
            return cup;
        }

        private static void BuildBoundaryStakes(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            var stakes = new GameObject("Out Of Bounds Stakes").transform;
            stakes.SetParent(parent, false);
            IReadOnlyList<Vec2d> poly = hole.PlayableBoundary;
            const double spacing = 8.0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vec2d a = poly[i];
                Vec2d b = poly[(i + 1) % poly.Count];
                double len = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                int count = Math.Max(1, (int)(len / spacing));
                for (int k = 0; k < count; k++)
                {
                    double f = (double)k / count;
                    double x = a.x + (b.x - a.x) * f;
                    double z = a.z + (b.z - a.z) * f;
                    GameObject stake = Primitive(PrimitiveType.Cylinder, "Stake", stakes, assets.BoundaryStake, true);
                    stake.transform.localScale = new Vector3(0.06f, 0.45f, 0.06f);
                    stake.transform.position = new Vector3((float)x, (float)world.GroundHeight(x, z) + 0.45f, (float)z);
                }
            }
        }

        private static void AddMesh(Transform parent, string name, Mesh mesh, Material material, bool receiveShadows, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = receiveShadows;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        /// <summary>Primitive without a collider: visuals never participate in scoring physics.</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material, bool castShadows)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) DestroyAny(collider);
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        private static GameObject InstantiateModel(GameObject model, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var linked = UnityEditor.PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
                if (linked != null) return linked;
            }
#endif
            return UnityEngine.Object.Instantiate(model, parent);
        }

        private static void FitHeight(GameObject instance, float targetHeight)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || targetHeight <= 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y < 1e-3f) return;
            instance.transform.localScale *= targetHeight / bounds.size.y;
        }

        private static void GetBounds(HoleDefinition hole, float margin, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = float.MaxValue;
            maxX = maxZ = float.MinValue;
            foreach (Vec2d p in hole.PlayableBoundary)
            {
                minX = Mathf.Min(minX, (float)p.x);
                minZ = Mathf.Min(minZ, (float)p.z);
                maxX = Mathf.Max(maxX, (float)p.x);
                maxZ = Mathf.Max(maxZ, (float)p.z);
            }
            minX -= margin;
            minZ -= margin;
            maxX += margin;
            maxZ += margin;
        }

        private static void DestroyAny(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
