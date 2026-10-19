using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Decorative groves of oak and pine in the out-of-bounds band between the holes' playable boundaries and the baked
    /// scenery forest, with a shrub understory. Nothing here is in play (every tree stands clear of every hole's
    /// boundary), so the trees carry no collision. Clumped by low-frequency noise so they read as stands, not a lattice;
    /// kept off the sea cliffs, cart paths, tee cameras and the holes' own trees. Trees stay model instances so each keeps
    /// its own LODGroup (the GPU Resident Drawer batches them); the understory is merged per tile like the ground cover.
    /// </summary>
    public static partial class CourseVisualBuilder
    {
        private const float GroveCell = 7f;
        private const float GroveNoiseScale = 1f / 46f;
        private const double GroveBoundaryClearM = 4.0;
        private const double GroveFullDensityM = 14.0;
        private const double GroveTeeClearM = 26.0;
        private const double GrovePathClearM = 4.5;
        private const double GroveTreeClearM = 7.0;
        private const double GroveCliffClearM = 14.0;
        private const int GroveCapPerHole = 90;

        private static void BuildGroves(HoleDefinition hole, ICollisionWorld world, CourseVisualAssets assets, Transform parent, CourseTiling tiling, int index)
        {
            if (assets.BroadleafModels.Count == 0 && assets.PineModels.Count == 0) return;
            var rng = new System.Random(StableSeed(hole.Id + "/groves"));
            var batch = new ScatterBatch(assets);
            var groves = new GameObject("Groves").transform;
            groves.SetParent(parent, false);
            List<Vec2d> cliff = Shapes.CatmullRom(GrayboxCourse.CliffEdgeControl, 4);
            var placed = new List<Vector2>();
            int trees = 0;

            for (float z = tiling.MinZ + GroveCell * 0.5f; z < tiling.MaxZ && trees < GroveCapPerHole; z += GroveCell)
            {
                for (float x = tiling.MinX + GroveCell * 0.5f; x < tiling.MaxX && trees < GroveCapPerHole; x += GroveCell)
                {
                    double px = x + (rng.NextDouble() - 0.5) * GroveCell * 0.7, pz = z + (rng.NextDouble() - 0.5) * GroveCell * 0.7;
                    double roll = rng.NextDouble(), pick = rng.NextDouble(), spin = rng.NextDouble(), size = rng.NextDouble();
                    if (tiling.OwnerAt(px, pz) != index) continue;

                    double outside = tiling.DistanceToOtherHole(-1, px, pz);
                    if (outside < GroveBoundaryClearM) continue;
                    float clump = Mathf.PerlinNoise((float)px * GroveNoiseScale + 31.7f, (float)pz * GroveNoiseScale + 8.3f);
                    double p = Smooth(0.42, 0.68, clump) * 0.8 * Smooth(GroveBoundaryClearM, GroveFullDensityM, outside);
                    if (roll >= p) continue;

                    double h = world.GroundHeight(px, pz);
                    if (h < GrayboxCourse.SeaLevelM + 3.0 || world.GroundNormal(px, pz).y < 0.86) continue;
                    if (world.HazardAt(px, pz) != null) continue;
                    if (TerrainModel.SignedDistanceLeft(cliff, px, pz) > -GroveCliffClearM) continue;
                    if (!ClearOfCourse(tiling, px, pz)) continue;
                    if (TooClose(placed, px, pz, 5.0)) continue;

                    // Pines on the higher, drier ground and in the second noise's patches; oaks elsewhere.
                    float kind = Mathf.PerlinNoise((float)px * GroveNoiseScale * 0.6f + 2.1f, (float)pz * GroveNoiseScale * 0.6f + 54.2f);
                    List<GameObject> models = (kind > 0.55f && assets.PineModels.Count > 0) || assets.BroadleafModels.Count == 0
                        ? assets.PineModels
                        : assets.BroadleafModels;
                    GameObject model = models[(int)(pick * models.Count)];
                    GameObject tree = InstantiateModel(model, groves);
                    tree.name = $"grove-{trees:00}";
                    tree.transform.SetPositionAndRotation(new Vector3((float)px, (float)h - 0.15f, (float)pz), Quaternion.Euler(0, (float)spin * 360f, 0));
                    tree.transform.localScale = model.transform.localScale * (0.72f + (float)size * 0.42f);
                    assets.ApplyFoliage(tree);
                    placed.Add(new Vector2((float)px, (float)pz));
                    trees++;

                    AddUnderstory(batch, assets, world, rng, px, pz);
                }
            }
            batch.Build(groves, "Understory", tiling);
        }

        /// <summary>One to three shrubs around a grove tree's drip line.</summary>
        private static void AddUnderstory(ScatterBatch batch, CourseVisualAssets assets, ICollisionWorld world, System.Random rng, double x, double z)
        {
            if (assets.ShrubModels.Count == 0) return;
            int count = 1 + (int)(rng.NextDouble() * 3);
            for (int i = 0; i < count; i++)
            {
                double angle = rng.NextDouble() * Math.PI * 2, radius = 2.5 + rng.NextDouble() * 3.0;
                double sx = x + Math.Cos(angle) * radius, sz = z + Math.Sin(angle) * radius;
                double pick = rng.NextDouble(), size = rng.NextDouble(), spin = rng.NextDouble();
                if (world.GroundNormal(sx, sz).y < 0.82) continue;
                GameObject shrub = assets.ShrubModels[(int)(pick * assets.ShrubModels.Count)];
                var at = new Vector3((float)sx, (float)world.GroundHeight(sx, sz) - 0.05f, (float)sz);
                batch.Add(shrub, Matrix4x4.TRS(at, Quaternion.Euler(0, (float)spin * 360f, 0), Vector3.one * (0.8f + (float)size * 0.7f)), false);
            }
        }

        /// <summary>Clear of every hole's tee (and the camera behind it), authored trees, and the whole cart path network.</summary>
        private static bool ClearOfCourse(CourseTiling tiling, double x, double z)
        {
            foreach (HoleDefinition other in tiling.Holes)
            {
                Vector3 tee = other.TeeM.ToUnity();
                if ((x - tee.x) * (x - tee.x) + (z - tee.z) * (z - tee.z) < GroveTeeClearM * GroveTeeClearM) return false;
                foreach (TreeObstacle tree in other.Trees)
                {
                    double dx = x - tree.Position.x, dz = z - tree.Position.z;
                    double clear = GroveTreeClearM + tree.CanopyRadius;
                    if (dx * dx + dz * dz < clear * clear) return false;
                }
                Vec2d park = CourseLayout.TeePark(other), pathByTee = CourseLayout.CartPath[CourseLayout.Nearest(tee.x, tee.z)];
                if (SegmentDistance(x, z, pathByTee, park) < GrovePathClearM + 2.0) return false;
            }
            double pathClear = CourseLayout.CartPathHalfWidthM + GrovePathClearM;
            foreach (CartPathLine line in CourseLayout.Lines)
            {
                IReadOnlyList<Vec2d> samples = line.Samples;
                int n = samples.Count, last = line.Closed ? n : n - 1;
                for (int k = 0; k < last; k++)
                {
                    if (SegmentDistance(x, z, samples[k], samples[(k + 1) % n]) < pathClear) return false;
                }
            }
            return true;
        }

        private static bool TooClose(List<Vector2> placed, double x, double z, double gap)
        {
            foreach (Vector2 p in placed)
            {
                if ((p.x - x) * (p.x - x) + (p.y - z) * (p.y - z) < gap * gap) return true;
            }
            return false;
        }

        private static double Smooth(double edge0, double edge1, double v)
        {
            double t = Math.Max(0, Math.Min(1, (v - edge0) / (edge1 - edge0)));
            return t * t * (3 - 2 * t);
        }
    }
}
