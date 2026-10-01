using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>
    /// Analytic, smooth heightfield shared by the solver and the renderer so what players see matches what the ball hits.
    /// Features apply in order: plateaus, tilts and bowls add; shelves blend toward their absolute height.
    /// </summary>
    public static class TerrainModel
    {
        public static double Height(IReadOnlyList<TerrainFeature> features, double x, double z)
        {
            double h = 0;
            for (int i = 0; i < features.Count; i++)
            {
                TerrainFeature f = features[i];
                switch (f.Kind)
                {
                    case TerrainFeatureKind.Plateau:
                    case TerrainFeatureKind.Tilt:
                    {
                        double dx = x - f.Center.x;
                        double dz = z - f.Center.z;
                        double w = Falloff(Math.Sqrt(dx * dx + dz * dz), f.InnerRadius, f.OuterRadius);
                        if (w <= 0) continue;
                        if (f.Kind == TerrainFeatureKind.Plateau) h += f.Height * w;
                        else h += (f.Slope.x * dx + f.Slope.z * dz) * w;
                        break;
                    }
                    case TerrainFeatureKind.Shelf:
                    {
                        if (f.Path == null || f.Path.Count < 2) continue;
                        double w = Ramp(SignedDistanceLeft(f.Path, x, z), f.InnerRadius, f.OuterRadius);
                        if (w > 0) h += (f.Height - h) * w;
                        break;
                    }
                    case TerrainFeatureKind.Bowl:
                    {
                        if (f.Path == null || f.Path.Count < 3 || !Shapes.Contains(f.Path, x, z)) continue;
                        h += f.Height * Ramp(DistanceToEdge(f.Path, x, z), 0, f.InnerRadius);
                        break;
                    }
                }
            }
            return h;
        }

        public static Vec3d Normal(IReadOnlyList<TerrainFeature> features, double x, double z)
        {
            const double e = 0.02;
            double hx = Height(features, x + e, z) - Height(features, x - e, z);
            double hz = Height(features, x, z + e) - Height(features, x, z - e);
            return new Vec3d(-hx, 2 * e, -hz).Normalized;
        }

        /// <summary>
        /// Distance to the polyline, positive on its left (cross(direction, offset) &gt; 0). The first and last segments
        /// extend as rays so the feature continues past the ends of the authored path.
        /// </summary>
        public static double SignedDistanceLeft(IReadOnlyList<Vec2d> path, double x, double z)
        {
            int n = path.Count;
            double best = double.MaxValue;
            double sign = 1;
            for (int i = 0; i < n - 1; i++)
            {
                Vec2d a = path[i], b = path[i + 1];
                double abx = b.x - a.x, abz = b.z - a.z;
                double len2 = abx * abx + abz * abz;
                if (len2 < 1e-12) continue;
                double t = ((x - a.x) * abx + (z - a.z) * abz) / len2;
                int vertex = -1;
                if (t < 0 && i > 0) { t = 0; vertex = i; }
                else if (t > 1 && i < n - 2) { t = 1; vertex = i + 1; }
                double dx = x - (a.x + abx * t), dz = z - (a.z + abz * t);
                double d2 = dx * dx + dz * dz;
                if (d2 >= best) continue;
                best = d2;
                if (vertex < 0)
                {
                    sign = abx * dz - abz * dx >= 0 ? 1 : -1;
                }
                else
                {
                    // At a shared vertex the side comes from the averaged left normal of both segments.
                    Vec2d p = path[vertex - 1], v = path[vertex], q = path[vertex + 1];
                    LeftNormal(p, v, out double n1x, out double n1z);
                    LeftNormal(v, q, out double n2x, out double n2z);
                    sign = (x - v.x) * (n1x + n2x) + (z - v.z) * (n1z + n2z) >= 0 ? 1 : -1;
                }
            }
            return best == double.MaxValue ? 0 : sign * Math.Sqrt(best);
        }

        /// <summary>Unsigned distance from a point to the closest edge of a closed polygon.</summary>
        public static double DistanceToEdge(IReadOnlyList<Vec2d> polygon, double x, double z)
        {
            double best = double.MaxValue;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vec2d a = polygon[j], b = polygon[i];
                double abx = b.x - a.x, abz = b.z - a.z;
                double len2 = abx * abx + abz * abz;
                double t = len2 > 1e-12 ? ((x - a.x) * abx + (z - a.z) * abz) / len2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double dx = x - (a.x + abx * t), dz = z - (a.z + abz * t);
                double d2 = dx * dx + dz * dz;
                if (d2 < best) best = d2;
            }
            return Math.Sqrt(best);
        }

        private static void LeftNormal(Vec2d a, Vec2d b, out double nx, out double nz)
        {
            double dx = b.x - a.x, dz = b.z - a.z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-12) { nx = nz = 0; return; }
            nx = -dz / len;
            nz = dx / len;
        }

        /// <summary>1 inside inner radius, smoothstep down to 0 at outer radius.</summary>
        private static double Falloff(double r, double inner, double outer)
        {
            if (r <= inner) return 1;
            if (r >= outer) return 0;
            double t = (r - inner) / (outer - inner);
            double s = 1 - t;
            return s * s * (3 - 2 * s);
        }

        /// <summary>0 at or below <paramref name="start"/>, smoothstep up to 1 at <paramref name="end"/>.</summary>
        private static double Ramp(double d, double start, double end)
        {
            if (d <= start) return 0;
            if (d >= end) return 1;
            double t = (d - start) / (end - start);
            return t * t * (3 - 2 * t);
        }
    }
}
