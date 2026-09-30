using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;

namespace Puttshack.Simulation
{
    public struct TreeHit
    {
        /// <summary>Fraction along the swept segment where the ball first touches the trunk.</summary>
        public double T;
        public Vec3d Point;
        /// <summary>Horizontal outward normal of the trunk at the contact.</summary>
        public Vec3d Normal;
        public string TreeId;
    }

    /// <summary>Immutable collision/query world for one hole. The solver never reads renderer state.</summary>
    public interface ICollisionWorld
    {
        HoleDefinition Hole { get; }
        double GroundHeight(double x, double z);
        Vec3d GroundNormal(double x, double z);
        /// <summary>Priority-ordered surface classification; hazards report Water.</summary>
        SurfaceId SurfaceAt(double x, double z);
        HazardVolume HazardAt(double x, double z);
        bool InBounds(double x, double z);
        bool SweepTrees(Vec3d from, Vec3d to, double ballRadius, out TreeHit hit);
    }

    public sealed class AnalyticWorld : ICollisionWorld
    {
        private readonly List<SurfaceRegion> _regionsByPriority;

        public HoleDefinition Hole { get; }

        public AnalyticWorld(HoleDefinition hole)
        {
            Hole = hole ?? throw new ArgumentNullException(nameof(hole));
            _regionsByPriority = new List<SurfaceRegion>(hole.Surfaces);
            _regionsByPriority.Sort((a, b) =>
            {
                int byPriority = b.Priority.CompareTo(a.Priority);
                return byPriority != 0 ? byPriority : string.CompareOrdinal(a.Id, b.Id);
            });
        }

        public double GroundHeight(double x, double z) => TerrainModel.Height(Hole.Terrain, x, z);

        public Vec3d GroundNormal(double x, double z) => TerrainModel.Normal(Hole.Terrain, x, z);

        public SurfaceId SurfaceAt(double x, double z)
        {
            if (HazardAt(x, z) != null) return SurfaceId.Water;
            foreach (SurfaceRegion region in _regionsByPriority)
            {
                if (Shapes.Contains(region.Polygon, x, z)) return region.Surface;
            }
            return Hole.DefaultSurface;
        }

        public HazardVolume HazardAt(double x, double z)
        {
            foreach (HazardVolume hazard in Hole.Hazards)
            {
                if (Shapes.Contains(hazard.Polygon, x, z)) return hazard;
            }
            return null;
        }

        public bool InBounds(double x, double z) => Shapes.Contains(Hole.PlayableBoundary, x, z);

        public bool SweepTrees(Vec3d from, Vec3d to, double ballRadius, out TreeHit hit)
        {
            hit = default;
            double best = double.MaxValue;
            double dx = to.x - from.x;
            double dz = to.z - from.z;
            double a = dx * dx + dz * dz;
            if (a < 1e-14) return false;

            foreach (TreeObstacle tree in Hole.Trees)
            {
                double r = tree.TrunkRadius + ballRadius;
                double fx = from.x - tree.Position.x;
                double fz = from.z - tree.Position.z;
                double b = 2 * (fx * dx + fz * dz);
                double c = fx * fx + fz * fz - r * r;
                if (c < 0) continue;
                double disc = b * b - 4 * a * c;
                if (disc < 0) continue;
                double t = (-b - Math.Sqrt(disc)) / (2 * a);
                if (t < 0 || t > 1 || t >= best) continue;

                Vec3d p = Vec3d.Lerp(from, to, t);
                double baseY = GroundHeight(tree.Position.x, tree.Position.z);
                if (p.y - ballRadius > baseY + tree.TrunkHeight) continue;

                best = t;
                hit = new TreeHit
                {
                    T = t,
                    Point = p,
                    Normal = new Vec3d(p.x - tree.Position.x, 0, p.z - tree.Position.z).Normalized,
                    TreeId = tree.Id,
                };
            }
            return best <= 1;
        }
    }
}
