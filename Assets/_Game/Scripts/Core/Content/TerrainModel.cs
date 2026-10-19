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
                        double w = Ramp(SignedDistanceLeft(f.Path, Index(f), x, z), f.InnerRadius, f.OuterRadius);
                        if (w > 0) h += (f.Height - h) * w;
                        break;
                    }
                    case TerrainFeatureKind.Bowl:
                    {
                        if (f.Path == null || f.Path.Count < 3 || !Index(f).Overlaps(x, z) || !Shapes.Contains(f.Path, x, z)) continue;
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
            for (int i = 0; i < n - 1; i++) Segment(path, i, x, z, ref best, ref sign);
            return best == double.MaxValue ? 0 : sign * Math.Sqrt(best);
        }

        /// <summary>
        /// <see cref="SignedDistanceLeft(IReadOnlyList{Vec2d}, double, double)"/> with whole groups of segments skipped when
        /// their (padded) bounds are no nearer than the best segment so far. Every skipped segment would have failed the
        /// <c>d2 &gt;= best</c> test anyway, so the winning segment, and the result, are bit-identical.
        /// </summary>
        private static double SignedDistanceLeft(List<Vec2d> path, PathBounds bounds, double x, double z)
        {
            double best = double.MaxValue;
            double sign = 1;
            int[] start = bounds.GroupStart;
            for (int g = 0; g + 1 < start.Length; g++)
            {
                if (bounds.GroupIsBounded[g] && bounds.GroupDistance2(g, x, z) * (1 - 1e-12) >= best) continue;
                for (int i = start[g]; i < start[g + 1]; i++) Segment(path, i, x, z, ref best, ref sign);
            }
            return best == double.MaxValue ? 0 : sign * Math.Sqrt(best);
        }

        private static void Segment(IReadOnlyList<Vec2d> path, int i, double x, double z, ref double best, ref double sign)
        {
            int n = path.Count;
            Vec2d a = path[i], b = path[i + 1];
            double abx = b.x - a.x, abz = b.z - a.z;
            double len2 = abx * abx + abz * abz;
            if (len2 < 1e-12) return;
            double t = ((x - a.x) * abx + (z - a.z) * abz) / len2;
            int vertex = -1;
            if (t < 0 && i > 0) { t = 0; vertex = i; }
            else if (t > 1 && i < n - 2) { t = 1; vertex = i + 1; }
            double dx = x - (a.x + abx * t), dz = z - (a.z + abz * t);
            double d2 = dx * dx + dz * dz;
            if (d2 >= best) return;
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

        private static PathBounds Index(TerrainFeature f)
        {
            if (f.PathIndex is PathBounds b && b.Matches(f.Path)) return b;
            b = new PathBounds(f.Path);
            f.PathIndex = b;
            return b;
        }

        /// <summary>
        /// Axis-aligned bounds of a feature path and of groups of its segments, padded far beyond rounding error so a point
        /// outside them provably gets nothing from the skipped geometry. The two end segments of a path extend as rays
        /// (see <see cref="SignedDistanceLeft(IReadOnlyList{Vec2d}, double, double)"/>) and are never skipped.
        /// </summary>
        private sealed class PathBounds
        {
            private const int GroupSize = 16;
            private const double Pad = 1e-6;

            public readonly int[] GroupStart;
            public readonly bool[] GroupIsBounded;
            private readonly List<Vec2d> _path;
            private readonly int _count;
            private readonly Vec2d _first, _last;
            private readonly double _minX, _minZ, _maxX, _maxZ;
            private readonly double[] _gMinX, _gMinZ, _gMaxX, _gMaxZ;

            public PathBounds(List<Vec2d> path)
            {
                _path = path;
                _count = path.Count;
                _first = path[0];
                _last = path[_count - 1];
                Bounds(path, 0, _count, out _minX, out _minZ, out _maxX, out _maxZ);

                // Segments: [0] alone (a ray), interior runs of GroupSize, [n - 2] alone (a ray).
                var starts = new List<int> { 0 };
                int segments = _count - 1;
                if (segments > 2)
                {
                    for (int s = 1; s < segments - 1; s += GroupSize) starts.Add(s);
                    starts.Add(segments - 1);
                }
                starts.Add(segments);
                GroupStart = starts.ToArray();
                int groups = GroupStart.Length - 1;
                GroupIsBounded = new bool[groups];
                _gMinX = new double[groups];
                _gMinZ = new double[groups];
                _gMaxX = new double[groups];
                _gMaxZ = new double[groups];
                for (int g = 0; g < groups; g++)
                {
                    int s0 = GroupStart[g], s1 = GroupStart[g + 1];
                    GroupIsBounded[g] = s0 > 0 && s1 < segments;
                    Bounds(path, s0, s1 + 1, out _gMinX[g], out _gMinZ[g], out _gMaxX[g], out _gMaxZ[g]);
                }
            }

            public bool Matches(List<Vec2d> path)
            {
                if (!ReferenceEquals(path, _path) || path.Count != _count) return false;
                Vec2d first = path[0], last = path[_count - 1];
                return first.x == _first.x && first.z == _first.z && last.x == _last.x && last.z == _last.z;
            }

            /// <summary>False only where the point is certainly outside the path's polygon (see <see cref="Shapes.Contains"/>).</summary>
            public bool Overlaps(double x, double z) => x >= _minX && x <= _maxX && z >= _minZ && z <= _maxZ;

            public double GroupDistance2(int g, double x, double z)
            {
                double dx = Math.Max(Math.Max(_gMinX[g] - x, x - _gMaxX[g]), 0);
                double dz = Math.Max(Math.Max(_gMinZ[g] - z, z - _gMaxZ[g]), 0);
                return dx * dx + dz * dz;
            }

            private static void Bounds(List<Vec2d> path, int from, int to, out double minX, out double minZ, out double maxX, out double maxZ)
            {
                minX = minZ = double.MaxValue;
                maxX = maxZ = double.MinValue;
                for (int i = from; i < to; i++)
                {
                    Vec2d p = path[i];
                    minX = Math.Min(minX, p.x);
                    maxX = Math.Max(maxX, p.x);
                    minZ = Math.Min(minZ, p.z);
                    maxZ = Math.Max(maxZ, p.z);
                }
                minX -= Pad;
                minZ -= Pad;
                maxX += Pad;
                maxZ += Pad;
            }
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
