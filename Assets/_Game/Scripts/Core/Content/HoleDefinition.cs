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
        /// <summary>Flat water surface height for sea hazards (the terrain below it reads as water); null drapes water on the terrain.</summary>
        public double? WaterLevelM;
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
        /// <summary>
        /// Cliff or shoreline along the polyline Path: on its left side (looking along the path) the height blends to the
        /// absolute Height, starting InnerRadius from the line and complete at OuterRadius. The end segments extend as rays.
        /// </summary>
        Shelf,
        /// <summary>Depression inside the polygon Path (a bunker): Height (negative) reached InnerRadius in from the edge.</summary>
        Bowl,
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
        public List<Vec2d> Path;

        /// <summary>Bounds of <see cref="Path"/> that <see cref="TerrainModel"/> builds on first use (not content: never serialized or hashed).</summary>
        [NonSerialized, JsonIgnore] internal object PathIndex;
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
        /// <summary>
        /// Authored hole this one replays as a stand-in (<see cref="CourseRouting"/>): its baked visuals are looked up by
        /// this id. Null when the hole is its own layout. Left out of the content hash so authored bakes stay valid.
        /// </summary>
        [JsonIgnore]
        public string LayoutId;

        [JsonIgnore]
        public string VisualId => LayoutId ?? Id;
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

        /// <summary>Open Catmull-Rom curve through the control points, <paramref name="samples"/> points per span.</summary>
        public static List<Vec2d> CatmullRom(IReadOnlyList<Vec2d> control, int samples)
        {
            var pts = new List<Vec2d>((control.Count - 1) * samples + 1);
            for (int i = 0; i < control.Count - 1; i++)
            {
                Vec2d p0 = control[Math.Max(0, i - 1)], p1 = control[i], p2 = control[i + 1], p3 = control[Math.Min(control.Count - 1, i + 2)];
                for (int s = 0; s < samples; s++) pts.Add(CatmullRomPoint(p0, p1, p2, p3, (double)s / samples));
            }
            pts.Add(control[control.Count - 1]);
            return pts;
        }

        /// <summary>Closed Catmull-Rom loop through the control points, <paramref name="samples"/> points per span.</summary>
        public static List<Vec2d> CatmullRomLoop(IReadOnlyList<Vec2d> control, int samples)
        {
            int n = control.Count;
            var pts = new List<Vec2d>(n * samples);
            for (int i = 0; i < n; i++)
            {
                Vec2d p0 = control[(i + n - 1) % n], p1 = control[i], p2 = control[(i + 1) % n], p3 = control[(i + 2) % n];
                for (int s = 0; s < samples; s++) pts.Add(CatmullRomPoint(p0, p1, p2, p3, (double)s / samples));
            }
            return pts;
        }

        private static Vec2d CatmullRomPoint(Vec2d p0, Vec2d p1, Vec2d p2, Vec2d p3, double t)
        {
            double t2 = t * t, t3 = t2 * t;
            return new Vec2d(
                0.5 * (2 * p1.x + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2 + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3),
                0.5 * (2 * p1.z + (-p0.z + p2.z) * t + (2 * p0.z - 5 * p1.z + 4 * p2.z - p3.z) * t2 + (-p0.z + 3 * p1.z - 3 * p2.z + p3.z) * t3));
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
