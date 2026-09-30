using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>Polygon on the XZ plane in hole-local meters. Higher priority wins where regions overlap.</summary>
    [Serializable]
    public sealed class SurfaceRegion
    {
        public string Id;
        public SurfaceId Surface;
        public int Priority;
        public List<Vec2d> Polygon = new List<Vec2d>();
    }

    [Serializable]
    public sealed class HazardVolume
    {
        public string Id;
        public List<Vec2d> Polygon = new List<Vec2d>();
        public string DropPointId;
    }

    [Serializable]
    public sealed class DropPoint
    {
        public string Id;
        public string HazardId;
        public Vec2d Position;
    }

    /// <summary>Trunks collide as vertical cylinders. Canopy is visual only in this graybox.</summary>
    [Serializable]
    public sealed class TreeObstacle
    {
        public string Id;
        public Vec2d Position;
        public double TrunkRadius;
        public double TrunkHeight;
        public double CanopyRadius;
        public double CanopyHeight;
    }

    public enum TerrainFeatureKind
    {
        /// <summary>Flat-topped raise: full Height inside InnerRadius, smoothstep to zero at OuterRadius.</summary>
        Plateau,
        /// <summary>Planar tilt (rise per meter along Slope) weighted by the same smooth falloff.</summary>
        Tilt,
    }

    [Serializable]
    public sealed class TerrainFeature
    {
        public TerrainFeatureKind Kind;
        public Vec2d Center;
        public double InnerRadius;
        public double OuterRadius;
        public double Height;
        public Vec2d Slope;
    }

    [Serializable]
    public sealed class CupProfile
    {
        public string Id = "cup-standard";
        public double CupRadius = 0.054;
        /// <summary>Game-friendly capture radius around the cup center for a ball center crossing (TUNE).</summary>
        public double CaptureRadius = 0.075;
        /// <summary>A crossing faster than this lips out instead of dropping (TUNE).</summary>
        public double CaptureSpeed = 1.55;
        public double LipRadius = 0.11;
        public double LipSpeedRetention = 0.8;
    }

    [Serializable]
    public sealed class HoleBriefing
    {
        public string Hazard;
        public string SafePlan;
        public string SuggestedClubId;
    }

    /// <summary>Versioned content package for one hole (spec section 25). Any collision change needs a new version.</summary>
    [Serializable]
    public sealed class HoleDefinition
    {
        public string Id;
        public string Version;
        public string Name;
        public int Number;
        public int Par;
        public int YardageYd;
        public Vec3d TeeM;
        public Vec3d CupM;
        public CupProfile Cup = new CupProfile();
        public List<Vec3d> PlayRouteM = new List<Vec3d>();
        public SurfaceId DefaultSurface = SurfaceId.LightRough;
        public List<SurfaceRegion> Surfaces = new List<SurfaceRegion>();
        public List<HazardVolume> Hazards = new List<HazardVolume>();
        public List<DropPoint> Drops = new List<DropPoint>();
        public List<Vec2d> PlayableBoundary = new List<Vec2d>();
        public List<TreeObstacle> Trees = new List<TreeObstacle>();
        public List<TerrainFeature> Terrain = new List<TerrainFeature>();
        public Vec3d WindMps;
        public HoleBriefing Briefing = new HoleBriefing();
        public string ContentHash;
    }

    [Serializable]
    public sealed class CourseDefinition
    {
        public string Id;
        public string Version;
        public string Name;
        public List<HoleDefinition> Holes = new List<HoleDefinition>();
        public string ContentHash;

        [JsonIgnore]
        public List<int> Pars
        {
            get
            {
                var pars = new List<int>(Holes.Count);
                foreach (HoleDefinition h in Holes) pars.Add(h.Par);
                return pars;
            }
        }
    }

    public static class Shapes
    {
        public static List<Vec2d> Ellipse(double cx, double cz, double rx, double rz, int segments = 40, double rotationRad = 0)
        {
            var pts = new List<Vec2d>(segments);
            double c = Math.Cos(rotationRad);
            double s = Math.Sin(rotationRad);
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                double lx = Math.Cos(a) * rx;
                double lz = Math.Sin(a) * rz;
                pts.Add(new Vec2d(cx + lx * c - lz * s, cz + lx * s + lz * c));
            }
            return pts;
        }

        public static List<Vec2d> Rect(double minX, double minZ, double maxX, double maxZ) => new List<Vec2d>
        {
            new Vec2d(minX, minZ), new Vec2d(maxX, minZ), new Vec2d(maxX, maxZ), new Vec2d(minX, maxZ),
        };

        public static bool Contains(IReadOnlyList<Vec2d> polygon, double x, double z)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vec2d a = polygon[i];
                Vec2d b = polygon[j];
                if ((a.z > z) != (b.z > z))
                {
                    double xCross = (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x;
                    if (x < xCross) inside = !inside;
                }
            }
            return inside;
        }
    }
}
