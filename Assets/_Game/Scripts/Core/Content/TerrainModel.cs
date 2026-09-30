using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>
    /// Analytic, smooth heightfield shared by the solver and the renderer so what players see matches what the ball hits.
    /// </summary>
    public static class TerrainModel
    {
        public static double Height(IReadOnlyList<TerrainFeature> features, double x, double z)
        {
            double h = 0;
            for (int i = 0; i < features.Count; i++)
            {
                TerrainFeature f = features[i];
                double dx = x - f.Center.x;
                double dz = z - f.Center.z;
                double w = Falloff(Math.Sqrt(dx * dx + dz * dz), f.InnerRadius, f.OuterRadius);
                if (w <= 0) continue;
                switch (f.Kind)
                {
                    case TerrainFeatureKind.Plateau:
                        h += f.Height * w;
                        break;
                    case TerrainFeatureKind.Tilt:
                        h += (f.Slope.x * dx + f.Slope.z * dz) * w;
                        break;
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

        /// <summary>1 inside inner radius, smoothstep down to 0 at outer radius.</summary>
        private static double Falloff(double r, double inner, double outer)
        {
            if (r <= inner) return 1;
            if (r >= outer) return 0;
            double t = (r - inner) / (outer - inner);
            double s = 1 - t;
            return s * s * (3 - 2 * s);
        }
    }
}
