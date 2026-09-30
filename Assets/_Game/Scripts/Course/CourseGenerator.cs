using System;
using System.Collections.Generic;
using RealGolf.Data;
using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>
    /// Builds a deterministic 18-hole, par-72 layout from a single seed so every
    /// player in a hot-seat round competes on an identical course.
    /// </summary>
    public static class CourseGenerator
    {
        private static readonly int[] ParPattern =
        {
            4, 5, 4, 3, 4, 4, 5, 3, 4,
            4, 3, 5, 4, 4, 3, 4, 5, 4
        };

        /// <param name="seed">Determines the whole layout.</param>
        /// <param name="name">
        /// Display name, shown on the scorecard and the results screen. Optional so
        /// a seed can still be generated on its own; CourseDefinition's own default
        /// name stands when this is null.
        /// </param>
        public static CourseDefinition Generate(int seed, string name = null)
        {
            var course = new CourseDefinition { Seed = seed };
            if (!string.IsNullOrEmpty(name))
            {
                course.Name = name;
            }

            var random = new System.Random(seed);

            for (int i = 0; i < ParPattern.Length; i++)
            {
                course.Holes.Add(BuildHole(i + 1, ParPattern[i], random.Next()));
            }

            return course;
        }

        private static HoleDefinition BuildHole(int number, int par, int seed)
        {
            var rng = new System.Random(seed);
            var hole = new HoleDefinition
            {
                Number = number,
                Par = par,
                Seed = seed,
                FairwayWidth = par == 3 ? 24f : Rand(rng, 26f, 34f)
            };

            float length = par switch
            {
                3 => Rand(rng, 120f, 190f),
                4 => Rand(rng, 300f, 410f),
                _ => Rand(rng, 450f, 540f)
            };

            int segments = par switch { 3 => 1, 4 => 2, _ => 3 };
            BuildCenterline(hole, rng, length, segments);

            hole.TeePosition = hole.Centerline[0];
            hole.GreenCenter = hole.Centerline[^1];
            hole.GreenRadius = Rand(rng, 11f, 16f);

            // Keep the cup away from the collar so approach shots can hold the green.
            float cupAngle = Rand(rng, 0f, Mathf.PI * 2f);
            float cupDist = Rand(rng, 0f, hole.GreenRadius * 0.5f);
            hole.CupPosition = hole.GreenCenter + new Vector2(Mathf.Cos(cupAngle), Mathf.Sin(cupAngle)) * cupDist;

            AddGreensideBunkers(hole, rng);
            AddFairwayHazards(hole, rng, par);
            ComputePlotBounds(hole);
            ScatterTrees(hole, rng);

            return hole;
        }

        private static void BuildCenterline(HoleDefinition hole, System.Random rng, float length, int segments)
        {
            Vector2 cursor = Vector2.zero;
            float heading = 90f; // degrees, +Z
            hole.Centerline.Add(cursor);

            float remaining = length;
            for (int i = 0; i < segments; i++)
            {
                bool last = i == segments - 1;
                float segLength = last ? remaining : remaining * Rand(rng, 0.45f, 0.6f);
                remaining -= segLength;

                if (i > 0)
                {
                    // Dogleg: bend the corridor left or right at each corner.
                    heading += Rand(rng, 14f, 32f) * (rng.Next(2) == 0 ? -1f : 1f);
                }

                float rad = heading * Mathf.Deg2Rad;
                cursor += new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * segLength;
                hole.Centerline.Add(cursor);
            }
        }

        private static void AddGreensideBunkers(HoleDefinition hole, System.Random rng)
        {
            int count = rng.Next(2, 4);
            float baseAngle = Rand(rng, 0f, Mathf.PI * 2f);

            for (int i = 0; i < count; i++)
            {
                float angle = baseAngle + i * (Mathf.PI * 2f / count) + Rand(rng, -0.35f, 0.35f);
                float radius = Rand(rng, 5f, 8f);
                float distance = hole.GreenRadius + radius + Rand(rng, 1.5f, 5f);
                Vector2 center = hole.GreenCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                hole.Bunkers.Add(new CircleRegion(center, radius));
            }
        }

        private static void AddFairwayHazards(HoleDefinition hole, System.Random rng, int par)
        {
            if (par == 3)
            {
                if (rng.NextDouble() < 0.35)
                {
                    AddWaterNearCenterline(hole, rng, Rand(rng, 0.45f, 0.7f));
                }

                return;
            }

            int fairwayBunkers = rng.Next(1, 4);
            for (int i = 0; i < fairwayBunkers; i++)
            {
                float t = Rand(rng, 0.3f, 0.85f);
                Vector2 point = SampleCenterline(hole, t, out Vector2 normal);
                float side = rng.Next(2) == 0 ? -1f : 1f;
                float offset = hole.FairwayWidth * 0.5f + Rand(rng, 1f, 7f);
                hole.Bunkers.Add(new CircleRegion(point + normal * side * offset, Rand(rng, 4.5f, 8f)));
            }

            if (rng.NextDouble() < 0.4)
            {
                AddWaterNearCenterline(hole, rng, Rand(rng, 0.4f, 0.8f));
            }
        }

        private static void AddWaterNearCenterline(HoleDefinition hole, System.Random rng, float t)
        {
            Vector2 point = SampleCenterline(hole, t, out Vector2 normal);
            float side = rng.Next(2) == 0 ? -1f : 1f;
            float radius = Rand(rng, 14f, 26f);
            float offset = hole.FairwayWidth * 0.5f + radius * Rand(rng, 0.55f, 0.9f);
            hole.Water.Add(new CircleRegion(point + normal * side * offset, radius));
        }

        /// <summary>Point at normalised distance t along the centreline, plus its left-hand normal.</summary>
        public static Vector2 SampleCenterline(HoleDefinition hole, float t, out Vector2 normal)
        {
            float target = Mathf.Clamp01(t) * hole.LengthMetres;
            float travelled = 0f;

            for (int i = 1; i < hole.Centerline.Count; i++)
            {
                Vector2 a = hole.Centerline[i - 1];
                Vector2 b = hole.Centerline[i];
                float segLength = Vector2.Distance(a, b);

                if (travelled + segLength >= target || i == hole.Centerline.Count - 1)
                {
                    float local = segLength < 1e-4f ? 0f : (target - travelled) / segLength;
                    Vector2 dir = (b - a).normalized;
                    normal = new Vector2(-dir.y, dir.x);
                    return Vector2.Lerp(a, b, Mathf.Clamp01(local));
                }

                travelled += segLength;
            }

            normal = Vector2.right;
            return hole.Centerline[^1];
        }

        private static void ComputePlotBounds(HoleDefinition hole)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            void Encapsulate(Vector2 p, float pad)
            {
                min = Vector2.Min(min, p - Vector2.one * pad);
                max = Vector2.Max(max, p + Vector2.one * pad);
            }

            foreach (Vector2 p in hole.Centerline)
            {
                Encapsulate(p, 62f);
            }

            Encapsulate(hole.GreenCenter, hole.GreenRadius + 45f);

            foreach (CircleRegion b in hole.Bunkers)
            {
                Encapsulate(b.Center, b.Radius + 12f);
            }

            foreach (CircleRegion w in hole.Water)
            {
                Encapsulate(w.Center, w.Radius + 12f);
            }

            hole.PlotBounds = new Rect(min, max - min);
        }

        private static void ScatterTrees(HoleDefinition hole, System.Random rng)
        {
            int attempts = 900;
            float corridor = hole.FairwayWidth * 0.5f + 9f;

            for (int i = 0; i < attempts && hole.Trees.Count < 190; i++)
            {
                var p = new Vector2(
                    Rand(rng, hole.PlotBounds.xMin, hole.PlotBounds.xMax),
                    Rand(rng, hole.PlotBounds.yMin, hole.PlotBounds.yMax));

                if (hole.DistanceToCenterline(p) < corridor)
                {
                    continue;
                }

                if (Vector2.Distance(p, hole.GreenCenter) < hole.GreenRadius + 14f)
                {
                    continue;
                }

                if (Vector2.Distance(p, hole.TeePosition) < 16f)
                {
                    continue;
                }

                bool blocked = false;
                foreach (CircleRegion w in hole.Water)
                {
                    if (Vector2.Distance(p, w.Center) < w.Radius + 5f)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    continue;
                }

                foreach (CircleRegion b in hole.Bunkers)
                {
                    if (Vector2.Distance(p, b.Center) < b.Radius + 4f)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                {
                    hole.Trees.Add(p);
                }
            }
        }

        private static float Rand(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);
    }
}
