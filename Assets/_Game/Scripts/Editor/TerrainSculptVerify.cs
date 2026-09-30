using System.Collections.Generic;
using RealGolf.Course;
using RealGolf.Data;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// Batch check on the terrain shaping ported from the Blender sculpt: tee shelf,
    /// green crown, bunker lip, collar ring, and mounding that only builds in the
    /// rough. Reports measurements rather than asserting, so the numbers can be read.
    /// </summary>
    public static class TerrainSculptVerify
    {
        public static void Run()
        {
            Debug.Log("SCULPT-BEGIN");
            CourseDefinition course = CourseGenerator.Generate(20240902);

            for (int i = 0; i < 3; i++)
            {
                HoleDefinition hole = course.Holes[i];
                var terrain = new HoleTerrain(hole);
                Vector2 dir = (hole.GreenCenter - hole.TeePosition).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x);

                // Tee shelf: the tee against ground 30 m to the side of it.
                float teeH = terrain.SampleHeight(hole.TeePosition);
                float offTee = terrain.SampleHeight(hole.TeePosition + dir * (TeeRadiusProbe + 6f));

                // Green crown: middle against the putting surface edge.
                float mid = terrain.SampleHeight(hole.GreenCenter);
                float rim = terrain.SampleHeight(hole.GreenCenter + side * hole.GreenRadius * 0.9f);

                // Mounding: height variance on the fairway against out in the rough.
                float fairVar = Variance(terrain, hole, dir, side, 0f);
                float roughVar = Variance(terrain, hole, dir, side, hole.FairwayWidth * 0.5f + 26f);

                Debug.Log($"SCULPT hole={hole.Number} par={hole.Par} len={hole.LengthMetres:F0}m " +
                          $"teeShelf={teeH - offTee:+0.00;-0.00}m " +
                          $"greenCrown={mid - rim:+0.00;-0.00}m " +
                          $"heightSpread fairway={fairVar:F2} rough={roughVar:F2}");

                // Bunker lip: a ray out of the first bunker.
                if (hole.Bunkers.Count > 0)
                {
                    CircleRegion b = hole.Bunkers[0];
                    var sb = new System.Text.StringBuilder($"SCULPT hole={hole.Number} bunker r={b.Radius:F1} cut:");
                    foreach (float k in new[] { 0f, 0.5f, 0.95f, 1.15f, 1.35f, 1.8f, 3f })
                    {
                        Vector2 at = b.Center + side * (b.Radius * k);
                        // Excavation only: the landform under the bunker cancels out.
                        float cut = terrain.SampleHeight(at) - terrain.SampleGroundHeight(at);
                        sb.Append($" {k:0.00}r={cut:+0.00;-0.00}");
                    }
                    Debug.Log(sb.ToString());
                }

                // Surface sequence walking out of the green: expect Green -> Collar -> ...
                var seq = new List<string>();
                SurfaceType last = SurfaceType.OutOfBounds;
                for (float d = 0f; d < hole.GreenRadius + 26f; d += 0.75f)
                {
                    SurfaceType s = terrain.SampleSurface(hole.GreenCenter - dir * d);
                    if (s != last)
                    {
                        seq.Add($"{s}@{d:F1}m");
                        last = s;
                    }
                }
                Debug.Log($"SCULPT hole={hole.Number} out of green: {string.Join(" -> ", seq)}");

                // Cross-section at mid-hole: mounding should build once past the mown
                // edge and stay damped on it. Heights are relative to the centreline.
                Vector2 mid2 = hole.TeePosition + dir * (hole.LengthMetres * 0.5f);
                float baseH = terrain.SampleGroundHeight(mid2);
                var xs = new System.Text.StringBuilder($"SCULPT hole={hole.Number} cross-section (mown half={hole.FairwayWidth * 0.5f:F0}m):");
                for (float off = -60f; off <= 60f; off += 10f)
                {
                    Vector2 at = mid2 + side * off;
                    xs.Append($" {off:+00;-00}={terrain.SampleGroundHeight(at) - baseH:+0.0;-0.0}");
                }
                Debug.Log(xs.ToString());
            }

            // Sand must sit in the hollow it is drawn on, for every hole.
            int mismatches = 0, sandSamples = 0;
            foreach (HoleDefinition hole in course.Holes)
            {
                var terrain = new HoleTerrain(hole);
                var rng = new System.Random(hole.Seed);
                for (int n = 0; n < 4000; n++)
                {
                    var p = new Vector2(
                        Mathf.Lerp(hole.PlotBounds.xMin, hole.PlotBounds.xMax, (float)rng.NextDouble()),
                        Mathf.Lerp(hole.PlotBounds.yMin, hole.PlotBounds.yMax, (float)rng.NextDouble()));
                    if (terrain.SampleSurface(p) != SurfaceType.Bunker) continue;
                    sandSamples++;
                    // Inside sand the ground must be below the surrounding lip line.
                    float here = terrain.SampleHeight(p);
                    float ground = terrain.SampleGroundHeight(p);
                    if (here > ground + 0.01f) mismatches++;
                }
            }

            Debug.Log($"SCULPT sand-in-hollow: {sandSamples} sand samples, {mismatches} above ground level");
            Debug.Log("SCULPT-END");
        }

        private const float TeeRadiusProbe = 6f;

        /// <summary>
        /// Local roughness of a line down the hole: mean absolute step between
        /// consecutive samples 6 m apart. Mounding shows up as a bigger step in the
        /// rough than on the mown corridor, independent of the broad landform.
        /// </summary>
        private static float Variance(HoleTerrain terrain, HoleDefinition hole, Vector2 dir, Vector2 side, float offset)
        {
            float total = 0f;
            int steps = 0;
            float previous = 0f;
            bool have = false;
            for (float t = 40f; t < hole.LengthMetres - 40f; t += 6f)
            {
                Vector2 at = hole.TeePosition + dir * t + side * offset;
                float h = terrain.SampleGroundHeight(at);
                if (have)
                {
                    total += Mathf.Abs(h - previous);
                    steps++;
                }

                previous = h;
                have = true;
            }

            return steps == 0 ? 0f : total / steps;
        }
    }
}
