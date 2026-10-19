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
    /// Course furniture (tee sign, bench, ball washer, bin, yardage posts, bunker rakes) and scattered ground cover
    /// (fescue tufts, shrubs, ice plant on the cliff tops, flowers). Everything is visual only and deterministic per hole;
    /// ground cover is merged into a few meshes per 48 m tile and material, culled by distance with an LOD group.
    /// </summary>
    public static partial class CourseVisualBuilder
    {
        private const float ScatterTile = 48f;

        private static void BuildProps(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent)
        {
            var root = new GameObject("Props").transform;
            root.SetParent(parent, false);
            Vector3 tee = hole.TeeM.ToUnity();
            Vector3 fwd = PlayDirection(hole);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vec2d park = CourseLayout.TeePark(hole);
            float side = Vector3.Dot(new Vector3((float)park.x - tee.x, 0, (float)park.z - tee.z), right) >= 0 ? 1f : -1f;
            Vector3 s = right * side;

            Vector3 signAt = tee + s * 5.6f - fwd * 1.2f;
            GameObject sign = Place(assets.TeeSignModel, "Tee Sign", root, world, signAt, tee - signAt);
            if (sign != null) Label(sign, hole, assets);
            Place(assets.BallWasherModel, "Ball Washer", root, world, tee + s * 5.8f + fwd * 0.6f, tee - (tee + s * 5.8f));
            Place(assets.TrashBinModel, "Trash Bin", root, world, tee + s * 5.9f - fwd * 2.9f, -s);
            Vector3 benchAt = tee + s * 7.4f - fwd * 5.5f;
            Place(assets.BenchModel, "Bench", root, world, benchAt, (tee + fwd * 6f) - benchAt);

            if (assets.YardagePostModel != null && hole.Par >= 4)
            {
                foreach (int yards in new[] { 200, 150, 100 })
                {
                    if (!PointOnRoute(hole, Units.YardsToMeters(yards), out Vector3 at, out Vector3 dir)) continue;
                    Vector3 across = Vector3.Cross(Vector3.up, dir) * side;
                    Vector3 p = at;
                    for (int k = 0; k < 40 && world.SurfaceAt(p.x, p.z) == SurfaceId.Fairway; k++) p += across;
                    p += across * 1.5f;
                    Place(assets.YardagePostModel, $"Yardage {yards}", root, world, p, -dir);
                }
            }

            if (assets.RakeModel != null)
            {
                Vector3 cup = hole.CupM.ToUnity();
                int n = 0;
                foreach (SurfaceRegion region in hole.Surfaces)
                {
                    if (region.Surface != SurfaceId.Bunker) continue;
                    Vector2 c = Vector2.zero;
                    foreach (Vec2d v in region.Polygon) c += new Vector2((float)v.x, (float)v.z);
                    c /= region.Polygon.Count;
                    Vector2 away = (c - new Vector2(cup.x, cup.z)).normalized;
                    if (away.sqrMagnitude < 0.5f) away = Vector2.up;
                    // Walk out of the sand, then a little further onto the grass.
                    Vector2 q = c;
                    for (int k = 0; k < 60 && Shapes.Contains(region.Polygon, q.x, q.y); k++) q += away * 0.2f;
                    q += away * 0.55f;
                    var tangent = new Vector3(-away.y, 0, away.x);
                    Place(assets.RakeModel, $"Rake {++n}", root, world, new Vector3(q.x, 0, q.y), tangent);
                }
            }
        }

        private static GameObject Place(GameObject model, string name, Transform parent, ICollisionWorld world, Vector3 at, Vector3 facing)
        {
            if (model == null) return null;
            GameObject instance = InstantiateModel(model, parent);
            instance.name = name;
            facing.y = 0;
            instance.transform.SetPositionAndRotation(
                new Vector3(at.x, (float)world.GroundHeight(at.x, at.z), at.z),
                facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity);
            return instance;
        }

        /// <summary>Hole number, name, par and yardage lettered on the tee sign's plaque (its Sign_Label marker).</summary>
        private static void Label(GameObject sign, HoleDefinition hole, CourseVisualAssets assets)
        {
            Transform anchor = FindDeep(sign.transform, "Sign_Label");
            var go = new GameObject("Sign Text", typeof(RectTransform));
            go.transform.SetParent(anchor != null ? anchor : sign.transform, false);
            if (anchor == null) go.transform.localPosition = new Vector3(0, 0.78f, 0.04f);
            // TextMeshPro reads from its -Z side; the plaque faces +Z.
            go.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            var text = go.AddComponent<TMPro.TextMeshPro>();
            if (assets.SignFont != null) text.font = assets.SignFont;
            text.rectTransform.sizeDelta = new Vector2(0.52f, 0.38f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 1.6f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.05f, 0.04f, 0.12f);
            text.text = $"<size=210%>{hole.Number}</size>\n{hole.Name.ToUpperInvariant()}\nPAR {hole.Par}   {hole.YardageYd} YDS";
            text.ForceMeshUpdate();

            // Keep the laid-out glyph mesh and drop the TextMeshPro component, so the sign bakes as a plain mesh asset.
            Mesh glyphs = UnityEngine.Object.Instantiate(text.mesh);
            glyphs.name = "sign_text";
            Material material = text.fontSharedMaterial;
            if (Application.isPlaying) UnityEngine.Object.Destroy(text);
            else UnityEngine.Object.DestroyImmediate(text);
            MeshFilter filter = go.GetComponent<MeshFilter>();
            if (filter == null) filter = go.AddComponent<MeshFilter>();
            filter.hideFlags = HideFlags.None;
            filter.sharedMesh = glyphs;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.hideFlags = HideFlags.None;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>The point <paramref name="fromCup"/> metres back from the cup along the play route.</summary>
        private static bool PointOnRoute(HoleDefinition hole, double fromCup, out Vector3 at, out Vector3 dir)
        {
            at = dir = default;
            var route = hole.PlayRouteM;
            double left = fromCup;
            for (int i = route.Count - 1; i > 0; i--)
            {
                Vector3 b = route[i].ToUnity(), a = route[i - 1].ToUnity();
                Vector3 ab = a - b;
                ab.y = 0;
                float len = ab.magnitude;
                if (len >= left)
                {
                    at = b + ab * (float)(left / len);
                    dir = -ab / len;
                    return true;
                }
                left -= len;
            }
            return false;
        }

        // ---------------------------------------------------------------- ground cover

        /// <summary>
        /// Adds the GPU-instanced grass field (<see cref="GrassField"/>) to each baked hole; the merged rough tufts then
        /// thin out to accents on the bunker faces and cliff tops.
        /// </summary>
        public static bool InstancedGrass = true;

        private static void BuildScatter(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent, CourseTiling tiling, int index, SurfaceMasks masks)
        {
            bool instancedGrass = InstancedGrass && BuildGrass(hole, world, parent);
            if (assets.GrassModels.Count == 0 && assets.ShrubModels.Count == 0 && assets.IcePlantModels.Count == 0) return;
            var rng = new System.Random(StableSeed(hole.Id));
            var batch = new ScatterBatch(assets);
            List<Vec2d> cliff = Shapes.CatmullRom(GrayboxCourse.CliffEdgeControl, 4);
            IReadOnlyList<Vec2d> path = CourseLayout.CartPath;
            Vector3 tee = hole.TeeM.ToUnity();
            Vec2d park = CourseLayout.TeePark(hole), pathByTee = path[CourseLayout.Nearest(tee.x, tee.z)];
            const float cell = 2f;

            for (float z = tiling.MinZ + cell * 0.5f; z < tiling.MaxZ; z += cell)
            {
                for (float x = tiling.MinX + cell * 0.5f; x < tiling.MaxX; x += cell)
                {
                    double px = x + (rng.NextDouble() - 0.5) * cell, pz = z + (rng.NextDouble() - 0.5) * cell;
                    double roll = rng.NextDouble(), tuft = rng.NextDouble(), pick = rng.NextDouble(), spin = rng.NextDouble(), size = rng.NextDouble();
                    if (tiling.OwnerAt(px, pz) != index) continue;
                    double h = world.GroundHeight(px, pz);
                    if (h < GrayboxCourse.SeaLevelM + 2.0) continue;
                    Vec3d normal = world.GroundNormal(px, pz);
                    if (normal.y < 0.82) continue;
                    bool inBounds = world.InBounds(px, pz);
                    SurfaceId surface = inBounds ? world.SurfaceAt(px, pz) : SurfaceId.DeepRough;
                    if (surface != SurfaceId.LightRough && surface != SurfaceId.DeepRough) continue;
                    if (world.HazardAt(px, pz) != null) continue;

                    float sdG = masks.GreenDistance(px, pz), sdB = masks.BunkerDistance(px, pz), sdF = masks.FairwayDistance(px, pz);
                    if (sdG < 4.5f || sdF < 1.4f || sdB < 0.3f) continue;
                    double dTee = Math.Sqrt((px - tee.x) * (px - tee.x) + (pz - tee.z) * (pz - tee.z));
                    if (dTee < 9) continue;
                    Vec2d onPath = path[CourseLayout.Nearest(px, pz)];
                    double dPath = Math.Sqrt((px - onPath.x) * (px - onPath.x) + (pz - onPath.z) * (pz - onPath.z));
                    if (dPath < CourseLayout.CartPathHalfWidthM + 1.4) continue;
                    // Keep the cart's spur off the path and its parking spot clear.
                    if (SegmentDistance(px, pz, pathByTee, park) < 3.0) continue;

                    double sdCliff = TerrainModel.SignedDistanceLeft(cliff, px, pz);
                    bool coastal = sdCliff > -9 && sdCliff < -0.8;
                    bool deep = surface == SurfaceId.DeepRough;
                    bool bunkerFace = sdB < 2.4f;
                    bool nearTree = NearTree(hole, px, pz, out double treeGap);
                    double far = inBounds ? 0 : tiling.DistanceToOtherHole(-1, px, pz);
                    double thin = far > 22 ? 0.45 : 1.0;

                    GameObject model = null;
                    float scale = 1f;
                    bool shadows = false;
                    bool alignToGround = true;
                    if (coastal && assets.IcePlantModels.Count > 0 && roll < 0.07)
                    {
                        model = assets.IcePlantModels[(int)(pick * assets.IcePlantModels.Count)];
                        scale = 0.9f + (float)size * 0.7f;
                    }
                    else if (coastal && assets.FlowerModels.Count > 0 && roll < 0.09)
                    {
                        model = assets.FlowerModels[(int)(pick * assets.FlowerModels.Count)];
                        scale = 0.8f + (float)size * 0.5f;
                    }
                    else if (nearTree && treeGap < 2.5 && assets.ShrubModels.Count > 0 && roll < 0.3)
                    {
                        model = assets.ShrubModels[(int)(pick * assets.ShrubModels.Count)];
                        scale = 0.7f + (float)size * 0.6f;
                        shadows = true;
                        alignToGround = false;
                    }
                    else if (deep && assets.ShrubModels.Count > 0 && roll < 0.02 * thin)
                    {
                        model = assets.ShrubModels[(int)(pick * assets.ShrubModels.Count)];
                        scale = 0.8f + (float)size * 0.7f;
                        shadows = true;
                        alignToGround = false;
                    }
                    else if (assets.GrassModels.Count > 0)
                    {
                        double p = coastal ? 0.12 : bunkerFace ? 0.3 : deep ? 0.1 * thin : sdF < 5 ? 0.012 : 0.025;
                        if (instancedGrass && !coastal) p *= bunkerFace ? 0.6 : 0.35;
                        if (tuft < p)
                        {
                            model = assets.GrassModels[(int)(pick * assets.GrassModels.Count)];
                            scale = 0.85f + (float)size * (deep || bunkerFace ? 0.75f : 0.35f);
                        }
                    }
                    if (model == null) continue;

                    Quaternion yaw = Quaternion.Euler(0, (float)spin * 360f, 0);
                    Quaternion tilt = alignToGround ? Quaternion.FromToRotation(Vector3.up, normal.ToUnity()) : Quaternion.identity;
                    var at = new Vector3((float)px, (float)h - 0.02f, (float)pz);
                    batch.Add(model, Matrix4x4.TRS(at, tilt * yaw, Vector3.one * scale), shadows);
                }
            }
            batch.Build(parent, "Ground Cover", tiling);
        }

        /// <summary>
        /// The hole's grass field over its Ground mesh, with a bitmap of cells kept clear: hazards (ponds and the sea) and
        /// the cart path network plus the cart's spur to the tee park. Grass classes come from the mesh's surface masks.
        /// </summary>
        private static bool BuildGrass(HoleDefinition hole, ICollisionWorld world, Transform parent)
        {
            Transform groundNode = parent.Find("Ground");
            MeshFilter ground = groundNode != null ? groundNode.GetComponent<MeshFilter>() : null;
            var material = Resources.Load<Material>(GrassField.MaterialResource);
            if (ground == null || ground.sharedMesh == null || material == null) return false;

            Bounds bounds = ground.sharedMesh.bounds;
            const float cell = GrassField.ExclusionCell;
            var origin = new Vector2(bounds.min.x, bounds.min.z);
            int w = Mathf.CeilToInt(bounds.size.x / cell) + 1, h = Mathf.CeilToInt(bounds.size.z / cell) + 1;
            var bits = new byte[(w * h + 7) / 8];

            void Mark(int ix, int iz)
            {
                if (ix < 0 || iz < 0 || ix >= w || iz >= h) return;
                int bit = iz * w + ix;
                bits[bit >> 3] |= (byte)(1 << (bit & 7));
            }

            void MarkSegment(Vec2d a, Vec2d b, double radius)
            {
                int x0 = Mathf.FloorToInt((float)((Math.Min(a.x, b.x) - radius - origin.x) / cell));
                int x1 = Mathf.FloorToInt((float)((Math.Max(a.x, b.x) + radius - origin.x) / cell));
                int z0 = Mathf.FloorToInt((float)((Math.Min(a.z, b.z) - radius - origin.y) / cell));
                int z1 = Mathf.FloorToInt((float)((Math.Max(a.z, b.z) + radius - origin.y) / cell));
                if (x1 < 0 || z1 < 0 || x0 >= w || z0 >= h) return;
                for (int iz = Math.Max(0, z0); iz <= Math.Min(h - 1, z1); iz++)
                {
                    for (int ix = Math.Max(0, x0); ix <= Math.Min(w - 1, x1); ix++)
                    {
                        double x = origin.x + (ix + 0.5) * cell, z = origin.y + (iz + 0.5) * cell;
                        if (SegmentDistance(x, z, a, b) < radius + cell * 0.5) Mark(ix, iz);
                    }
                }
            }

            for (int iz = 0; iz < h; iz++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    double x = origin.x + (ix + 0.5) * cell, z = origin.y + (iz + 0.5) * cell;
                    if (world.HazardAt(x, z) != null) Mark(ix, iz);
                }
            }

            double pathClear = CourseLayout.CartPathHalfWidthM + 0.35;
            foreach (CartPathLine line in CourseLayout.Lines)
            {
                IReadOnlyList<Vec2d> samples = line.Samples;
                int n = samples.Count, last = line.Closed ? n : n - 1;
                for (int k = 0; k < last; k++) MarkSegment(samples[k], samples[(k + 1) % n], pathClear);
            }
            Vector3 tee = hole.TeeM.ToUnity();
            Vec2d park = CourseLayout.TeePark(hole), pathByTee = CourseLayout.CartPath[CourseLayout.Nearest(tee.x, tee.z)];
            MarkSegment(pathByTee, park, 2.6);

            var go = new GameObject("Grass");
            go.transform.SetParent(parent, false);
            go.AddComponent<GrassField>().Configure(ground, material, StableSeed(hole.Id), (float)GrayboxCourse.SeaLevelM + 1.5f, origin, w, h, bits);
            return true;
        }

        private static bool NearTree(HoleDefinition hole, double x, double z, out double gap)
        {
            gap = double.MaxValue;
            foreach (TreeObstacle tree in hole.Trees)
            {
                double d = Math.Sqrt((x - tree.Position.x) * (x - tree.Position.x) + (z - tree.Position.z) * (z - tree.Position.z)) - tree.TrunkRadius;
                if (d < gap) gap = d;
            }
            return gap < 6;
        }

        private static double SegmentDistance(double x, double z, Vec2d a, Vec2d b)
        {
            double dx = b.x - a.x, dz = b.z - a.z;
            double len2 = dx * dx + dz * dz;
            double t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - a.x) * dx + (z - a.z) * dz) / len2));
            double ex = x - (a.x + dx * t), ez = z - (a.z + dz * t);
            return Math.Sqrt(ex * ex + ez * ez);
        }

        private static int StableSeed(string text)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in text) h = h * 31 + c;
                return h;
            }
        }

        /// <summary>Collects model sub-meshes by material and tile, then merges each group into one renderer.</summary>
        private sealed class ScatterBatch
        {
            private readonly CourseVisualAssets _assets;
            private readonly Dictionary<(Material material, int tx, int tz, bool shadows), List<CombineInstance>> _groups =
                new Dictionary<(Material, int, int, bool), List<CombineInstance>>();
            public int Count { get; private set; }

            public ScatterBatch(CourseVisualAssets assets) => _assets = assets;

            public void Add(GameObject model, Matrix4x4 world, bool shadows)
            {
                Vector3 p = world.GetColumn(3);
                int tx = Mathf.FloorToInt(p.x / ScatterTile), tz = Mathf.FloorToInt(p.z / ScatterTile);
                Matrix4x4 toTile = Matrix4x4.Translate(-new Vector3(tx * ScatterTile, 0, tz * ScatterTile)) * world;
                Matrix4x4 rootInverse = model.transform.worldToLocalMatrix;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (mesh == null || renderer == null || !mesh.isReadable) continue;
                    Material[] materials = renderer.sharedMaterials;
                    Matrix4x4 m = toTile * rootInverse * filter.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Material material = _assets.ResolveScatterMaterial(materials[Mathf.Min(sub, materials.Length - 1)]);
                        if (material == null) continue;
                        var key = (material, tx, tz, shadows);
                        if (!_groups.TryGetValue(key, out List<CombineInstance> list)) _groups[key] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = m });
                    }
                }
                Count++;
            }

            public void Build(Transform parent, string name, CourseTiling tiling)
            {
                if (_groups.Count == 0) return;
                var root = new GameObject(name).transform;
                root.SetParent(parent, false);
                var tiles = new Dictionary<(int, int), Transform>();
                int part = 0;
                foreach (var group in _groups)
                {
                    var tileKey = (group.Key.tx, group.Key.tz);
                    if (!tiles.TryGetValue(tileKey, out Transform tile))
                    {
                        tile = new GameObject($"Tile {group.Key.tx}_{group.Key.tz}").transform;
                        tile.SetParent(root, false);
                        tile.position = new Vector3(group.Key.tx * ScatterTile, 0, group.Key.tz * ScatterTile);
                        tiles[tileKey] = tile;
                    }
                    var mesh = new Mesh { name = $"cover_{part++}", indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(group.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var go = new GameObject(group.Key.material.name);
                    go.transform.SetParent(tile, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = group.Key.material;
                    renderer.shadowCastingMode = group.Key.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                    // Tufts and flowers vanish past ~170 m (sub-pixel there); shrubs and ice plant hold to ~450 m.
                    var lod = go.AddComponent<LODGroup>();
                    bool small = !group.Key.shadows && !group.Key.material.name.StartsWith("PB_IcePlant", StringComparison.Ordinal);
                    lod.SetLODs(new[] { new LOD(small ? 0.28f : 0.1f, new Renderer[] { renderer }) });
                    lod.RecalculateBounds();
                }
            }
        }
    }
}
