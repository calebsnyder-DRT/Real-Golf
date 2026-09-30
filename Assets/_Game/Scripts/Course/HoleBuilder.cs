using System.Collections.Generic;
using RealGolf.Core;
using RealGolf.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealGolf.Course
{
    public sealed class BuiltHole
    {
        public GameObject Root;
        public HoleDefinition Definition;
        public HoleTerrain Terrain;
        public Vector3 TeeWorldPosition;
        public Vector3 CupWorldPosition;
        public Transform Flagstick;
    }

    /// <summary>Turns a <see cref="HoleDefinition"/> into scene geometry.</summary>
    public static class HoleBuilder
    {
        public const float CupCaptureRadius = 0.09f;
        private const float TargetCellSize = 1.5f;
        private const int MaxVerticesPerAxis = 400;

        // The plot only reaches ~62 m past the centreline, so without this the mesh
        // simply stopped and you saw sky under the horizon. The skirt carries the
        // same height field out far enough for fog to finish the job.
        private const float SurroundExtent = 1400f;
        private const float SurroundGrowth = 1.6f;
        private const float SurroundMaxStep = 200f;
        private const int SurroundMaxRings = 18;

        // Cells a surface boundary crosses are subdivided this many times, taking
        // the edge from 1.5 m blocks to under 0.4 m where -- and only where -- it
        // shows. Interior cells are untouched.
        private const int BoundaryRefineDepth = 2;
        private const float RefineCellLimit = 4f;

        /// <summary>
        /// Binds gameplay to a hole that was already generated in the editor. The
        /// terrain field is analytic and cheap to reconstruct from the definition, so
        /// nothing is rebuilt: this only re-derives the tee, cup and flagstick the
        /// game needs from meshes that are already in the scene.
        /// </summary>
        public static BuiltHole Rebind(HoleDefinition definition, Transform bakedRoot)
        {
            var terrain = new HoleTerrain(definition);
            return new BuiltHole
            {
                Root = bakedRoot != null ? bakedRoot.gameObject : null,
                Definition = definition,
                Terrain = terrain,
                TeeWorldPosition = terrain.ToWorld(definition.TeePosition),
                CupWorldPosition = terrain.ToWorld(definition.CupPosition),
                Flagstick = bakedRoot != null ? bakedRoot.Find("Flagstick") : null
            };
        }

        public static BuiltHole Build(HoleDefinition definition)
        {
            var terrain = new HoleTerrain(definition);
            var root = new GameObject($"Hole_{definition.Number:00}");

            BuildTerrainMesh(root.transform, definition, terrain);
            BuildWater(root.transform, definition, terrain);
            BuildTrees(root.transform, definition, terrain);
            BuildProps(root.transform, definition, terrain);
            Transform flag = BuildCupAndFlag(root.transform, definition, terrain);
            BuildTeeMarkers(root.transform, definition, terrain);

            return new BuiltHole
            {
                Root = root,
                Definition = definition,
                Terrain = terrain,
                TeeWorldPosition = terrain.ToWorld(definition.TeePosition),
                CupWorldPosition = terrain.ToWorld(definition.CupPosition),
                Flagstick = flag
            };
        }

        private static void BuildTerrainMesh(Transform parent, HoleDefinition definition, HoleTerrain terrain)
        {
            Rect bounds = definition.PlotBounds;

            float[] xs = BuildAxis(bounds.xMin, bounds.xMax);
            float[] zs = BuildAxis(bounds.yMin, bounds.yMax);
            int countX = xs.Length;
            int countZ = zs.Length;

            int gridCount = countX * countZ;
            var verts = new List<Vector3>(gridCount + gridCount / 3);
            var uvs = new List<Vector2>(verts.Capacity);
            var surf = new List<SurfaceType>(verts.Capacity);

            SurfaceType Classify(Vector2 at)
            {
                SurfaceType surface = terrain.SampleSurface(at);
                return surface == SurfaceType.OutOfBounds ? SurfaceType.DeepRough : surface;
            }

            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    var planar = new Vector2(xs[x], zs[z]);
                    verts.Add(new Vector3(planar.x, terrain.SampleHeight(planar), planar.y));
                    uvs.Add(new Vector2(planar.x * 0.1f, planar.y * 0.1f));
                    surf.Add(Classify(planar));
                }
            }

            // One submesh per surface so each can carry its own material.
            var surfaceOrder = new List<SurfaceType>();
            var submeshes = new Dictionary<SurfaceType, List<int>>();

            List<int> IndicesFor(SurfaceType surface)
            {
                if (!submeshes.TryGetValue(surface, out List<int> list))
                {
                    list = new List<int>();
                    submeshes[surface] = list;
                    surfaceOrder.Add(surface);
                }

                return list;
            }

            // Splits an edge. The new vertex is the straight midpoint of the two it
            // came from rather than a fresh height sample, which is what keeps a
            // refined cell sitting exactly on the surface its unrefined neighbours
            // still describe -- no cracks along the join.
            int Mid(int a, int b)
            {
                Vector3 pa = verts[a];
                Vector3 pb = verts[b];
                var p = new Vector3((pa.x + pb.x) * 0.5f, (pa.y + pb.y) * 0.5f, (pa.z + pb.z) * 0.5f);
                verts.Add(p);
                uvs.Add((uvs[a] + uvs[b]) * 0.5f);
                surf.Add(Classify(new Vector2(p.x, p.z)));
                return verts.Count - 1;
            }

            // Cells wholly inside one surface cost nothing; only cells a boundary
            // actually crosses get subdivided, and only they pay for it.
            void Emit(int i00, int i10, int i01, int i11, int depth)
            {
                SurfaceType s = surf[i00];
                if (s == surf[i10] && s == surf[i01] && s == surf[i11])
                {
                    List<int> uniform = IndicesFor(s);
                    uniform.Add(i00);
                    uniform.Add(i01);
                    uniform.Add(i10);
                    uniform.Add(i10);
                    uniform.Add(i01);
                    uniform.Add(i11);
                    return;
                }

                if (depth <= 0)
                {
                    // Out of subdivisions: each triangle at least gets its own
                    // material from its own centroid.
                    List<int> a = IndicesFor(Classify(Centroid(verts[i00], verts[i01], verts[i10])));
                    a.Add(i00);
                    a.Add(i01);
                    a.Add(i10);

                    List<int> b = IndicesFor(Classify(Centroid(verts[i10], verts[i01], verts[i11])));
                    b.Add(i10);
                    b.Add(i01);
                    b.Add(i11);
                    return;
                }

                int bottom = Mid(i00, i10);
                int top = Mid(i01, i11);
                int left = Mid(i00, i01);
                int right = Mid(i10, i11);

                // Taken along the cell's own triangulation diagonal, so the patch
                // replaces the original surface rather than bulging off it.
                int middle = Mid(i01, i10);

                Emit(i00, bottom, left, middle, depth - 1);
                Emit(bottom, i10, middle, right, depth - 1);
                Emit(left, middle, i01, top, depth - 1);
                Emit(middle, right, top, i11, depth - 1);
            }

            for (int z = 0; z < countZ - 1; z++)
            {
                bool fineZ = zs[z + 1] - zs[z] < RefineCellLimit;

                for (int x = 0; x < countX - 1; x++)
                {
                    int i00 = z * countX + x;
                    int i10 = i00 + 1;
                    int i01 = i00 + countX;
                    int i11 = i01 + 1;

                    // The far skirt cells are hundreds of metres across; subdividing
                    // those would be pointless work behind a wall of fog.
                    bool fine = fineZ && xs[x + 1] - xs[x] < RefineCellLimit;
                    Emit(i00, i10, i01, i11, fine ? BoundaryRefineDepth : 0);
                }
            }

            var mesh = new Mesh
            {
                name = $"HoleTerrain_{definition.Number:00}",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = surfaceOrder.Count;

            var materials = new Material[surfaceOrder.Count];
            for (int i = 0; i < surfaceOrder.Count; i++)
            {
                mesh.SetTriangles(submeshes[surfaceOrder[i]], i);
                materials[i] = MaterialLibrary.ForSurface(surfaceOrder[i]);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static Vector2 Centroid(Vector3 a, Vector3 b, Vector3 c) =>
            new Vector2((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f);

        /// <summary>
        /// Grid line positions for one axis: an even run across the playable plot,
        /// then rings that grow geometrically outwards to the far horizon. Keeping
        /// the skirt inside the same mesh as the plot means the shared vertices are
        /// literally shared, so there is no seam to crack open and nothing to
        /// z-fight. Roughly 14 extra lines per side buy 1.4 km of ground.
        /// </summary>
        private static float[] BuildAxis(float min, float max)
        {
            int inner = Mathf.Min(MaxVerticesPerAxis, Mathf.CeilToInt((max - min) / TargetCellSize) + 1);
            float step = (max - min) / (inner - 1);

            var outward = new List<float>(SurroundMaxRings);
            float walk = step;
            float position = min;
            for (int i = 0; i < SurroundMaxRings && position > min - SurroundExtent; i++)
            {
                walk = Mathf.Min(walk * SurroundGrowth, SurroundMaxStep);
                position -= walk;
                outward.Add(position);
            }

            outward.Reverse();
            var coords = new List<float>(outward.Count * 2 + inner);
            coords.AddRange(outward);

            for (int i = 0; i < inner; i++)
            {
                coords.Add(min + i * step);
            }

            walk = step;
            position = max;
            for (int i = 0; i < SurroundMaxRings && position < max + SurroundExtent; i++)
            {
                walk = Mathf.Min(walk * SurroundGrowth, SurroundMaxStep);
                position += walk;
                coords.Add(position);
            }

            return coords.ToArray();
        }

        private static void BuildWater(Transform parent, HoleDefinition definition, HoleTerrain terrain)
        {
            foreach (CircleRegion water in definition.Water)
            {
                var go = new GameObject("Water");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(water.Center.x, terrain.WaterSurfaceHeight(water), water.Center.y);
                go.transform.localScale = new Vector3(water.Radius, 1f, water.Radius);

                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Disc();
                go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.WaterSurface;
            }
        }

        private static void BuildTrees(Transform parent, HoleDefinition definition, HoleTerrain terrain)
        {
            var container = new GameObject("Trees");
            container.transform.SetParent(parent, false);

            List<GameObject> prefabs = SelectSpecies(definition);
            var rng = new System.Random(definition.Seed + 7717);

            foreach (Vector2 position in definition.Trees)
            {
                Vector3 basePoint = terrain.ToWorld(position);
                Quaternion rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                GameObject prefab = prefabs.Count > 0 ? prefabs[rng.Next(prefabs.Count)] : null;
                bool conifer = IsConifer(prefab);

                // Mature course trees, not the 5-11 m shrubs this used to plant.
                // Conifers run taller and narrower than the broadleaves.
                float height = conifer
                    ? 11f + (float)rng.NextDouble() * 8f
                    : 8f + (float)rng.NextDouble() * 7f;

                GameObject tree = prefab != null
                    ? ModelLibrary.Spawn(prefab, container.transform, basePoint, height, rotation)
                    : null;

                if (tree == null)
                {
                    BuildProceduralTree(container.transform, basePoint, rotation, height, rng);
                    continue;
                }

                ApplyTreeMaterials(tree, conifer, rng);

                // Trunk collider so a wayward drive can be knocked down by the trees.
                var collider = tree.AddComponent<CapsuleCollider>();
                float scale = tree.transform.localScale.y;
                collider.height = scale > 0.0001f ? height / scale : height;
                collider.radius = collider.height * 0.055f;
                collider.center = new Vector3(0f, collider.height * 0.5f, 0f);
            }

            BuildDistantTreeline(container.transform, definition, terrain, prefabs, rng);
        }

        /// <summary>
        /// Woodland ringing the plot, thickest right against the boundary and
        /// thinning outwards. Without it the ground past the last in-play tree ran
        /// bare to the horizon, which is a large part of why the distance did not
        /// read. These carry no colliders -- they are well out of play.
        /// </summary>
        private static void BuildDistantTreeline(
            Transform parent, HoleDefinition definition, HoleTerrain terrain,
            List<GameObject> prefabs, System.Random rng)
        {
            if (prefabs.Count == 0)
            {
                return;
            }

            const float band = 240f;
            Rect b = definition.PlotBounds;
            float perimeter = 2f * (b.width + b.height);
            int count = Mathf.Clamp(Mathf.RoundToInt(perimeter / 11f), 50, 150);

            for (int i = 0; i < count; i++)
            {
                float t = (float)rng.NextDouble() * perimeter;
                Vector2 edge;
                Vector2 outward;

                if (t < b.width)
                {
                    edge = new Vector2(b.xMin + t, b.yMin);
                    outward = Vector2.down;
                }
                else if (t < b.width + b.height)
                {
                    edge = new Vector2(b.xMax, b.yMin + (t - b.width));
                    outward = Vector2.right;
                }
                else if (t < 2f * b.width + b.height)
                {
                    edge = new Vector2(b.xMax - (t - b.width - b.height), b.yMax);
                    outward = Vector2.up;
                }
                else
                {
                    edge = new Vector2(b.xMin, b.yMax - (t - 2f * b.width - b.height));
                    outward = Vector2.left;
                }

                // Squared falloff so most of them hug the boundary where they are
                // actually visible, rather than spreading evenly over 240 m.
                float push = 4f + Mathf.Pow((float)rng.NextDouble(), 2.2f) * band;
                var tangent = new Vector2(-outward.y, outward.x);
                Vector2 position = edge
                                   + outward * push
                                   + tangent * ((float)rng.NextDouble() - 0.5f) * 26f;

                GameObject prefab = prefabs[rng.Next(prefabs.Count)];
                bool conifer = IsConifer(prefab);
                float height = conifer
                    ? 13f + (float)rng.NextDouble() * 9f
                    : 10f + (float)rng.NextDouble() * 8f;

                GameObject tree = ModelLibrary.Spawn(
                    prefab, parent, terrain.ToWorld(position), height,
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));

                if (tree != null)
                {
                    ApplyTreeMaterials(tree, conifer, rng);
                }
            }
        }

        private static bool IsConifer(GameObject prefab) =>
            prefab != null && prefab.name.IndexOf("pine", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// A hole plants two or three species rather than drawing from all nine, so
        /// each treeline reads as one wood instead of a sample catalogue -- and each
        /// hole still differs from the last.
        /// </summary>
        private static List<GameObject> SelectSpecies(HoleDefinition definition)
        {
            var broadleaf = new List<GameObject>();
            var conifer = new List<GameObject>();

            foreach (GameObject prefab in ModelLibrary.Trees)
            {
                if (prefab == null)
                {
                    continue;
                }

                // tree_blocks is literally stacked cubes and tree_cone a bare cone.
                // Those silhouettes read as toys next to the daylight rig, so they
                // sit out unless nothing else loaded.
                if (prefab.name == "tree_blocks" || prefab.name == "tree_cone")
                {
                    continue;
                }

                (IsConifer(prefab) ? conifer : broadleaf).Add(prefab);
            }

            var rng = new System.Random(definition.Seed + 4409);
            var chosen = new List<GameObject>(4);
            Take(broadleaf, 2);
            Take(conifer, rng.Next(1, 3));

            if (chosen.Count == 0)
            {
                foreach (GameObject prefab in ModelLibrary.Trees)
                {
                    if (prefab != null)
                    {
                        chosen.Add(prefab);
                    }
                }
            }

            return chosen;

            void Take(List<GameObject> pool, int wanted)
            {
                for (int i = 0; i < wanted && pool.Count > 0; i++)
                {
                    int index = rng.Next(pool.Count);
                    chosen.Add(pool[index]);
                    pool.RemoveAt(index);
                }
            }
        }

        /// <summary>
        /// Swaps the kit's flat foliage and bark materials for a variant drawn from
        /// the course palette, so a treeline is a range of greens instead of one
        /// wall of a single colour. Replacing the material outright also means the
        /// trees come out right even if the kit has not been reimported yet.
        /// </summary>
        private static void ApplyTreeMaterials(GameObject tree, bool conifer, System.Random rng)
        {
            Material foliage = MaterialLibrary.Foliage(rng.Next(MaterialLibrary.FoliageVariants(conifer)), conifer);
            Material bark = MaterialLibrary.Bark(rng.Next(MaterialLibrary.BarkVariants(conifer)), conifer);

            foreach (MeshRenderer renderer in tree.GetComponentsInChildren<MeshRenderer>())
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < slots.Length; i++)
                {
                    string name = slots[i] != null ? slots[i].name : string.Empty;

                    bool isBark = name.IndexOf("wood", System.StringComparison.OrdinalIgnoreCase) >= 0
                                  || name.IndexOf("bark", System.StringComparison.OrdinalIgnoreCase) >= 0;

                    // Everything that is not trunk is treated as canopy rather than
                    // matched by name. tree_detailed carries a stray two-triangle face
                    // on Unity's white default material, and a leaf-name match leaves
                    // it glaring at seven times the brightness of the fairway.
                    slots[i] = isBark ? bark : foliage;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = slots;
                }
            }
        }

        /// <summary>Fallback tree used when the model kit is unavailable.</summary>
        private static void BuildProceduralTree(
            Transform parent, Vector3 basePoint, Quaternion rotation, float height, System.Random rng)
        {
            float radius = 0.55f + (float)rng.NextDouble() * 0.35f;

            var tree = new GameObject("Tree");
            tree.transform.SetParent(parent, false);
            tree.transform.position = basePoint;
            tree.transform.rotation = rotation;

            var trunk = new GameObject("Trunk");
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, height * 0.25f, 0f);
            trunk.transform.localScale = new Vector3(radius * 0.5f, height * 0.25f, radius * 0.5f);
            trunk.AddComponent<MeshFilter>().sharedMesh = PrimitiveMeshCache.Cylinder;
            trunk.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.TreeTrunk;

            var collider = tree.AddComponent<CapsuleCollider>();
            collider.height = height;
            collider.radius = radius * 0.6f;
            collider.center = new Vector3(0f, height * 0.5f, 0f);

            var foliage = new GameObject("Foliage");
            foliage.transform.SetParent(tree.transform, false);
            foliage.transform.localPosition = new Vector3(0f, height * 0.42f, 0f);
            foliage.transform.localScale = new Vector3(radius * 3.4f, height * 0.72f, radius * 3.4f);
            foliage.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Cone();
            foliage.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.TreeFoliage;
        }

        /// <summary>Scatters rocks and grass tufts through the rough for visual interest.</summary>
        private static void BuildProps(Transform parent, HoleDefinition definition, HoleTerrain terrain)
        {
            IReadOnlyList<GameObject> prefabs = ModelLibrary.Props;
            if (prefabs.Count == 0)
            {
                return;
            }

            var container = new GameObject("Props");
            container.transform.SetParent(parent, false);

            var rng = new System.Random(definition.Seed + 4231);
            float corridor = definition.FairwayWidth * 0.5f + 2f;
            int placed = 0;

            for (int attempt = 0; attempt < 600 && placed < 110; attempt++)
            {
                var p = new Vector2(
                    Mathf.Lerp(definition.PlotBounds.xMin, definition.PlotBounds.xMax, (float)rng.NextDouble()),
                    Mathf.Lerp(definition.PlotBounds.yMin, definition.PlotBounds.yMax, (float)rng.NextDouble()));

                SurfaceType surface = terrain.SampleSurface(p);
                if (surface != SurfaceType.Rough && surface != SurfaceType.DeepRough)
                {
                    continue;
                }

                if (definition.DistanceToCenterline(p) < corridor)
                {
                    continue;
                }

                float height = 0.35f + (float)rng.NextDouble() * 0.75f;
                Quaternion rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                ModelLibrary.Spawn(prefabs[rng.Next(prefabs.Count)], container.transform, terrain.ToWorld(p), height, rotation);
                placed++;
            }
        }

        private static Transform BuildCupAndFlag(Transform parent, HoleDefinition definition, HoleTerrain terrain)
        {
            Vector3 cup = terrain.ToWorld(definition.CupPosition);

            var cupGo = new GameObject("Cup");
            cupGo.transform.SetParent(parent, false);
            cupGo.transform.position = cup + Vector3.up * 0.01f;
            cupGo.transform.localScale = new Vector3(0.108f, 1f, 0.108f);
            cupGo.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Disc();
            cupGo.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CupBlack;

            var flagRoot = new GameObject("Flagstick");
            flagRoot.transform.SetParent(parent, false);
            flagRoot.transform.position = cup;

            // Regulation flagstick height.
            const float PinHeight = 2.134f;
            // The authored pin already carries its own pole/ferrule/pennant materials,
            // so it must not be flattened onto the kit's single atlas.
            GameObject authored = ModelLibrary.AuthoredFlagstick;
            GameObject baked = authored ?? ModelLibrary.BakedFlagstick;
            GameObject pin = ModelLibrary.Spawn(
                baked != null ? baked : ModelLibrary.Flag,
                flagRoot.transform, cup, PinHeight, Quaternion.identity);

            if (pin != null && baked != null && authored == null)
            {
                // The baked prop shares a single atlas across all of its parts.
                Material atlas = MaterialLibrary.Flagstick;
                foreach (MeshRenderer renderer in pin.GetComponentsInChildren<MeshRenderer>())
                {
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++)
                    {
                        materials[i] = atlas;
                    }

                    renderer.sharedMaterials = materials;
                }
            }

            if (pin == null)
            {
                var pole = new GameObject("Pole");
                pole.transform.SetParent(flagRoot.transform, false);
                pole.transform.localPosition = new Vector3(0f, PinHeight * 0.5f, 0f);
                pole.transform.localScale = new Vector3(0.03f, PinHeight * 0.5f, 0.03f);
                pole.AddComponent<MeshFilter>().sharedMesh = PrimitiveMeshCache.Cylinder;
                pole.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PoleWhite;

                var flag = new GameObject("Flag");
                flag.transform.SetParent(flagRoot.transform, false);
                flag.transform.localPosition = new Vector3(0.28f, PinHeight * 0.88f, 0f);
                flag.transform.localScale = new Vector3(0.55f, 0.34f, 0.02f);
                flag.AddComponent<MeshFilter>().sharedMesh = PrimitiveMeshCache.Cube;
                flag.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.FlagRed;
            }

            return flagRoot.transform;
        }

        private static void BuildTeeMarkers(Transform parent, HoleDefinition definition, HoleTerrain terrain)
        {
            CourseGenerator.SampleCenterline(definition, 0.02f, out Vector2 normal);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 planar = definition.TeePosition + normal * side * 2.2f;
                var marker = new GameObject("TeeMarker");
                marker.transform.SetParent(parent, false);
                marker.transform.position = terrain.ToWorld(planar) + Vector3.up * 0.12f;
                marker.transform.localScale = new Vector3(0.3f, 0.24f, 0.3f);
                marker.AddComponent<MeshFilter>().sharedMesh = PrimitiveMeshCache.Sphere;
                marker.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.TeeMarker;
            }
        }
    }

    /// <summary>
    /// Unity only exposes primitive meshes by creating a GameObject, so grab one
    /// of each up front and reuse the shared meshes everywhere.
    /// </summary>
    public static class PrimitiveMeshCache
    {
        private static Mesh _cylinder;
        private static Mesh _sphere;
        private static Mesh _cube;

        public static Mesh Cylinder => _cylinder ??= Extract(PrimitiveType.Cylinder);
        public static Mesh Sphere => _sphere ??= Extract(PrimitiveType.Sphere);
        public static Mesh Cube => _cube ??= Extract(PrimitiveType.Cube);

        private static Mesh Extract(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;

            if (Application.isPlaying)
            {
                Object.Destroy(temp);
            }
            else
            {
                Object.DestroyImmediate(temp);
            }

            return mesh;
        }
    }
}
