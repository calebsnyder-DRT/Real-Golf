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
    /// With the whole course given, the hole draws only its share of the course-wide ground grid (<see cref="CourseTiling"/>)
    /// and of the cart path, so all hole prefabs can be shown together without overlaps.
    /// </summary>
    public static partial class CourseVisualBuilder
    {
        public const float GroundSpacing = 0.8f;
        public const float GroundMargin = 30f;

        public static BakedHole Build(HoleDefinition hole, CourseVisualAssets assets, Transform parent) => Build(hole, null, assets, parent);

        public static BakedHole Build(HoleDefinition hole, CourseDefinition course, CourseVisualAssets assets, Transform parent)
        {
            var world = new AnalyticWorld(hole);
            CourseTiling tiling = TilingFor(course, hole, out int index);
            var root = new GameObject($"Hole {hole.Number:00} - {hole.Name}");
            root.transform.SetParent(parent, false);

            var baked = root.AddComponent<BakedHole>();
            baked.HoleId = hole.Id;
            baked.HoleVersion = hole.Version;
            baked.ContentHash = hole.ContentHash;

            var masks = new SurfaceMasks(hole);
            AddMesh(root.transform, "Ground", BuildGroundMesh(hole, world, tiling, index, masks), assets.Ground, receiveShadows: true, castShadows: false);
            Mesh lips = BuildBunkerLips(hole, world, masks);
            if (lips != null) AddMesh(root.transform, "Bunker Lips", lips, assets.Ground, receiveShadows: true, castShadows: true);

            // Sea hazards (flat WaterLevelM) are drawn by the baked coastal environment; ponds drape on the terrain.
            var hazardPolygons = new List<List<Vec2d>>();
            foreach (HazardVolume hazard in hole.Hazards)
            {
                if (!hazard.WaterLevelM.HasValue) hazardPolygons.Add(hazard.Polygon);
            }
            Mesh water = BuildOverlayMesh($"{hole.Id}_water", hole, world, hazardPolygons, 0.05f, true);
            if (water != null) AddMesh(root.transform, "Water", water, assets.Water, receiveShadows: false, castShadows: false);

            Mesh path = BuildCartPathMesh(hole, world, tiling, index);
            if (path != null) AddMesh(root.transform, "Cart Path", path, assets.CartPath, receiveShadows: true, castShadows: false);

            var trees = new GameObject("Trees").transform;
            trees.SetParent(root.transform, false);
            for (int i = 0; i < hole.Trees.Count; i++) BuildTree(hole.Trees[i], i, world, assets, trees);

            baked.Tee = BuildTee(hole, world, assets, root.transform);
            baked.Cup = BuildCupAndFlag(hole, world, assets, root.transform);
            BuildBoundaryStakes(hole, world, assets, root.transform, tiling, index);
            BuildPenaltyStakes(hole, world, assets, root.transform);
            BuildProps(hole, world, assets, root.transform);
            BuildScatter(hole, world, assets, root.transform, tiling, index, masks);
            BuildGroves(hole, world, assets, root.transform, tiling, index);
            return baked;
        }

        private static readonly Dictionary<string, CourseTiling> TilingCache = new Dictionary<string, CourseTiling>();

        /// <summary>The course-wide grid when the hole is part of <paramref name="course"/>; otherwise the hole on its own.</summary>
        public static CourseTiling TilingFor(CourseDefinition course, HoleDefinition hole, out int index)
        {
            index = course != null ? course.Holes.FindIndex(h => h.Id == hole.Id) : -1;
            if (index < 0)
            {
                index = 0;
                return new CourseTiling(new[] { hole }, GroundSpacing, GroundMargin);
            }
            string key = course.ContentHash ?? course.Version;
            if (!TilingCache.TryGetValue(key, out CourseTiling tiling))
            {
                tiling = new CourseTiling(course.Holes, GroundSpacing, GroundMargin);
                TilingCache[key] = tiling;
            }
            return tiling;
        }

        public static Mesh BuildGroundMesh(HoleDefinition hole, ICollisionWorld world) =>
            BuildGroundMesh(hole, world, new CourseTiling(new[] { hole }, GroundSpacing, GroundMargin), 0, new SurfaceMasks(hole));

        /// <summary>The hole's cells of the course grid, vertices shared within the hole and identical to its neighbours' on seams.</summary>
        private static Mesh BuildGroundMesh(HoleDefinition hole, ICollisionWorld world, CourseTiling tiling, int index, SurfaceMasks masks)
        {
            int nx = tiling.Nx, nz = tiling.Nz;
            var map = new int[nx * nz];
            for (int i = 0; i < map.Length; i++) map[i] = -1;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color32>();
            var stripes = new List<Vector2>();
            var masksA = new List<Vector2>();
            var masksB = new List<Vector2>();
            var along = new List<Vector2>();
            var triangles = new List<int>();
            Vector2 mow = masks.Along;

            int Vertex(int ix, int iz)
            {
                int key = iz * nx + ix;
                if (map[key] >= 0) return map[key];
                double x = tiling.MinX + ix * GroundSpacing;
                double z = tiling.MinZ + iz * GroundSpacing;
                bool inBounds = world.InBounds(x, z);
                vertices.Add(new Vector3((float)x, (float)world.GroundHeight(x, z), (float)z));
                normals.Add(world.GroundNormal(x, z).ToUnity());
                colors.Add(SurfaceWeights(inBounds ? world.SurfaceAt(x, z) : SurfaceId.DeepRough));
                stripes.Add(masks.Stripe(x, z));
                masks.Sample(x, z, inBounds, out Vector2 a, out Vector2 b);
                masksA.Add(a);
                masksB.Add(b);
                along.Add(mow);
                map[key] = vertices.Count - 1;
                return map[key];
            }

            for (int iz = 0; iz < nz - 1; iz++)
            {
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    if (tiling.CellOwner(ix, iz) != index) continue;
                    int a = Vertex(ix, iz);
                    int b = Vertex(ix + 1, iz);
                    int c = Vertex(ix, iz + 1);
                    int d = Vertex(ix + 1, iz + 1);
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = $"{hole.Id}_ground", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, stripes);
            mesh.SetUVs(1, masksA);
            mesh.SetUVs(2, masksB);
            mesh.SetUVs(3, along);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Per-vertex signed distances (meters, negative inside, clamped to +-<see cref="MaskRange"/>) to the green, bunker,
        /// fairway/tee and deep-rough regions, so the ground shader draws crisp outlines that follow the gameplay polygons
        /// instead of the 0.8 m grid. UV0 carries mowing-stripe coordinates along and across the line of play; UV3 the
        /// mowing direction, so stripes flip light and dark with the view like bent grass.
        /// </summary>
        private sealed class SurfaceMasks
        {
            public const float MaskRange = 6f;
            private readonly List<List<Vec2d>> _green = new List<List<Vec2d>>();
            private readonly List<List<Vec2d>> _bunker = new List<List<Vec2d>>();
            private readonly List<List<Vec2d>> _fairway = new List<List<Vec2d>>();
            private readonly List<List<Vec2d>> _deep = new List<List<Vec2d>>();
            private readonly Vec2d _origin;
            private readonly Vec2d _along;

            public SurfaceMasks(HoleDefinition hole)
            {
                foreach (SurfaceRegion region in hole.Surfaces)
                {
                    switch (region.Surface)
                    {
                        case SurfaceId.Green: _green.Add(region.Polygon); break;
                        case SurfaceId.Bunker: _bunker.Add(region.Polygon); break;
                        case SurfaceId.Fairway:
                        case SurfaceId.Tee: _fairway.Add(region.Polygon); break;
                        case SurfaceId.DeepRough: _deep.Add(region.Polygon); break;
                    }
                }
                _origin = new Vec2d(hole.TeeM.x, hole.TeeM.z);
                double dx = hole.CupM.x - hole.TeeM.x, dz = hole.CupM.z - hole.TeeM.z;
                double len = Math.Sqrt(dx * dx + dz * dz);
                _along = len > 1e-6 ? new Vec2d(dx / len, dz / len) : new Vec2d(0, 1);
            }

            public Vector2 Along => new Vector2((float)_along.x, (float)_along.z);

            public IReadOnlyList<List<Vec2d>> Bunkers => _bunker;

            public Vector2 Stripe(double x, double z)
            {
                double ox = x - _origin.x, oz = z - _origin.z;
                return new Vector2((float)(ox * _along.x + oz * _along.z), (float)(ox * _along.z - oz * _along.x));
            }

            public void Sample(double x, double z, bool inBounds, out Vector2 a, out Vector2 b)
            {
                a = new Vector2(Distance(_green, x, z), Distance(_bunker, x, z));
                b = new Vector2(Distance(_fairway, x, z), inBounds ? Distance(_deep, x, z) : -MaskRange);
            }

            public float GreenDistance(double x, double z) => Distance(_green, x, z);
            public float BunkerDistance(double x, double z) => Distance(_bunker, x, z);
            public float FairwayDistance(double x, double z) => Distance(_fairway, x, z);

            private static float Distance(List<List<Vec2d>> polygons, double x, double z)
            {
                double best = MaskRange;
                foreach (List<Vec2d> polygon in polygons)
                {
                    double d = TerrainModel.DistanceToEdge(polygon, x, z);
                    if (d > MaskRange && !Shapes.Contains(polygon, x, z)) continue;
                    if (Shapes.Contains(polygon, x, z)) d = -d;
                    if (d < best) best = d;
                }
                return (float)Math.Max(-MaskRange, best);
            }
        }

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
            bool cypress = tree.Id.StartsWith("cypress", StringComparison.Ordinal);
            List<GameObject> models = cypress && assets.CypressModels.Count > 0 ? assets.CypressModels : pine ? assets.PineModels : assets.BroadleafModels;

            if (models.Count > 0)
            {
                GameObject model = models[index % models.Count];
                GameObject instance = InstantiateModel(model, parent);
                instance.name = tree.Id;
                instance.transform.position = basePos;
                // Cypress models lean inland (+X) like the windswept coastal trees, so they only get a small twist.
                float yaw = cypress ? (index * 23f) % 50f - 25f : (index * 137.5f) % 360f;
                instance.transform.rotation = Quaternion.Euler(0, yaw, 0);
                FitHeight(instance, (float)tree.CanopyHeight);
                assets.ApplyFoliage(instance);
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
            Vector3 fwd = PlayDirection(hole);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            foreach (float side in new[] { -3f, 3f })
            {
                Vector3 at = hole.TeeM.ToUnity() + right * side + fwd * 2.5f;
                var ground = new Vector3(at.x, (float)world.GroundHeight(at.x, at.z), at.z);
                if (assets.TeeMarkerModel != null)
                {
                    // Turned peg ~10 cm tall with its base at the pivot.
                    GameObject model = InstantiateModel(assets.TeeMarkerModel, tee);
                    model.name = "Tee Marker";
                    model.transform.position = ground;
                    continue;
                }
                GameObject marker = Primitive(PrimitiveType.Cylinder, "Tee Marker", tee, assets.TeeMarker, true);
                marker.transform.position = ground + new Vector3(0, 0.05f, 0);
                marker.transform.localScale = new Vector3(0.06f, 0.05f, 0.06f);
            }
            return tee;
        }

        /// <summary>Horizontal direction from the tee along the first leg of the play route.</summary>
        public static Vector3 PlayDirection(HoleDefinition hole)
        {
            Vector3 tee = hole.TeeM.ToUnity();
            Vector3 next = hole.PlayRouteM.Count > 1 ? hole.PlayRouteM[1].ToUnity() : hole.CupM.ToUnity();
            Vector3 d = next - tee;
            d.y = 0;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        private static void BuildBoundaryStakes(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent, CourseTiling tiling, int index)
        {
            var stakes = new GameObject("Out Of Bounds Stakes").transform;
            stakes.SetParent(parent, false);
            IReadOnlyList<Vec2d> poly = hole.PlayableBoundary;
            const double spacing = 8.0;
            // Edges shared with a neighbouring hole are internal to the course and carry no stakes.
            const double neighbourGap = 16.0;
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
                    if (world.HazardAt(x, z) != null || BelowSea(hole, world, x, z)) continue;
                    if (tiling.DistanceToOtherHole(index, x, z) < neighbourGap) continue;
                    GameObject stake = Primitive(PrimitiveType.Cylinder, "Stake", stakes, assets.BoundaryStake, true);
                    stake.transform.localScale = new Vector3(0.06f, 0.45f, 0.06f);
                    stake.transform.position = new Vector3((float)x, (float)world.GroundHeight(x, z) + 0.45f, (float)z);
                }
            }
        }

        /// <summary>Red stakes along the playable edge of each hazard (inside the boundary), set just outside the line.</summary>
        private static void BuildPenaltyStakes(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            if (assets.PenaltyStake == null || hole.Hazards.Count == 0) return;
            var stakes = new GameObject("Penalty Area Stakes").transform;
            stakes.SetParent(parent, false);
            const double spacing = 14.0;
            foreach (HazardVolume hazard in hole.Hazards)
            {
                List<Vec2d> poly = hazard.Polygon;
                double next = spacing * 0.5, walked = 0;
                for (int i = 0; i < poly.Count; i++)
                {
                    Vec2d a = poly[i];
                    Vec2d b = poly[(i + 1) % poly.Count];
                    double dx = b.x - a.x, dz = b.z - a.z;
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    double start = walked;
                    walked += len;
                    if (len < 1e-6) continue;
                    for (; next < walked; next += spacing)
                    {
                        double s = next - start;
                        double x = a.x + dx * s / len, z = a.z + dz * s / len;
                        // Step off the line toward whichever side is playable.
                        double nx = -dz / len, nz = dx / len;
                        double px = x + nx * 0.6, pz = z + nz * 0.6;
                        if (world.HazardAt(px, pz) != null) { px = x - nx * 0.6; pz = z - nz * 0.6; }
                        if (!world.InBounds(px, pz) || world.HazardAt(px, pz) != null) continue;
                        GameObject stake = Primitive(PrimitiveType.Cylinder, "Penalty Stake", stakes, assets.PenaltyStake, true);
                        stake.transform.localScale = new Vector3(0.05f, 0.4f, 0.05f);
                        stake.transform.position = new Vector3((float)px, (float)world.GroundHeight(px, pz) + 0.4f, (float)pz);
                    }
                }
            }
        }

        private static bool BelowSea(HoleDefinition hole, ICollisionWorld world, double x, double z)
        {
            foreach (HazardVolume hazard in hole.Hazards)
            {
                if (hazard.WaterLevelM.HasValue && world.GroundHeight(x, z) < hazard.WaterLevelM.Value + 0.5) return true;
            }
            return world.GroundHeight(x, z) < GrayboxCourse.SeaLevelM + 0.5;
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

        private static void DestroyAny(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
