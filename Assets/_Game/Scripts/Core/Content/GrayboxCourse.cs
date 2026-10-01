using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    public static class GrayboxCourse
    {
        public const string CourseId = "puttshack-pro-graybox";
        public const string CourseVersion = "graybox-0.2";

        /// <summary>Proposed par-72 layout (spec sections 26-27, Hole 1 corrected to par 4).</summary>
        public static readonly IReadOnlyList<int> ProposedPars = new[] { 4, 4, 3, 4, 5, 4, 3, 4, 5, 4, 3, 5, 4, 4, 3, 4, 5, 4 };

        /// <summary>Sea surface of Stillwater Cove in hole-local meters (the clifftop near the green sits about 11 m above it).</summary>
        public const double SeaLevelM = -10.0;

        public static CourseDefinition Create()
        {
            var course = new CourseDefinition
            {
                Id = CourseId,
                Version = CourseVersion,
                Name = "Puttshack Pro - Graybox",
            };
            course.Holes.Add(StillwaterCliffs());
            ContentHasher.Seal(course);
            return course;
        }

        /// <summary>Cliff top edge along the cove (the sea is on its left, looking down the hole).</summary>
        public static readonly Vec2d[] CliffEdgeControl =
        {
            new Vec2d(-42, -110), new Vec2d(-38, -60), new Vec2d(-34, -25), new Vec2d(-32, 5), new Vec2d(-31.5, 30),
            new Vec2d(-33, 52), new Vec2d(-37, 70), new Vec2d(-34, 88), new Vec2d(-28.5, 104), new Vec2d(-24.5, 120),
            new Vec2d(-23.5, 136), new Vec2d(-25, 152), new Vec2d(-29, 166), new Vec2d(-35, 178), new Vec2d(-41, 192),
            new Vec2d(-45, 210), new Vec2d(-44, 235), new Vec2d(-38, 260),
        };

        /// <summary>Foot of the beach: past this line the sea floor falls away (narrow and rocky under the green).</summary>
        public static readonly Vec2d[] ShoreControl =
        {
            new Vec2d(-52, -110), new Vec2d(-47, -60), new Vec2d(-43, -25), new Vec2d(-41, 5), new Vec2d(-40, 30),
            new Vec2d(-41.5, 52), new Vec2d(-45, 70), new Vec2d(-42, 88), new Vec2d(-35, 104), new Vec2d(-30, 120),
            new Vec2d(-28.5, 136), new Vec2d(-30, 152), new Vec2d(-35, 166), new Vec2d(-43, 178), new Vec2d(-52, 192),
            new Vec2d(-60, 210), new Vec2d(-62, 235), new Vec2d(-58, 260),
        };

        /// <summary>
        /// Hole 3 "Stillwater Cliffs", par 3, 165 yd, after Pebble Beach's cliff-top par 3s: a downhill shot from a raised
        /// tee to a small green perched above Stillwater Cove. The Pacific (a penalty area from the cliff edge out) runs the
        /// whole left side; bunkers guard the green, Monterey cypress frame it, a cart path runs right.
        /// </summary>
        public static HoleDefinition StillwaterCliffs()
        {
            var hole = new HoleDefinition
            {
                Id = "h03-garden-green",
                Version = "pebble-0.3",
                Name = "Stillwater Cliffs",
                Number = 3,
                Par = 3,
                YardageYd = 165,
                DefaultSurface = SurfaceId.LightRough,
                WindMps = new Vec3d(1.4, 0, -0.9),
                Briefing = new HoleBriefing
                {
                    Hazard = "The Pacific runs the whole left side: anything over the cliff edge is in the penalty area.",
                    SafePlan = "Aim at the right half of the green. Short and right leaves an easy chip from the fairway.",
                    SuggestedClubId = "iron5",
                },
            };

            List<Vec2d> cliff = Shapes.CatmullRom(CliffEdgeControl, 4);
            List<Vec2d> shore = Shapes.CatmullRom(ShoreControl, 4);

            var greenShape = Shapes.Ellipse(-5, 150, 12, 10.5, 48, -0.3);
            var bunkerFrontLeft = Shapes.Ellipse(-16, 140, 5.0, 2.8, 32, 0.9);
            var bunkerFrontRight = Shapes.Ellipse(8, 139, 5.5, 3.0, 32, -0.4);
            var bunkerBackRight = Shapes.Ellipse(5, 163, 4.5, 2.5, 32, 0.3);
            var bunkerPot = Shapes.Ellipse(-11, 124, 3.0, 2.1, 28, 0.2);

            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Plateau, Center = new Vec2d(-2, -12), InnerRadius = 22, OuterRadius = 90, Height = 4.5 });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Plateau, Center = new Vec2d(0, -1), InnerRadius = 6.5, OuterRadius = 12, Height = 0.9 });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Plateau, Center = new Vec2d(-5, 150), InnerRadius = 15, OuterRadius = 30, Height = 1.0 });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Tilt, Center = new Vec2d(-5, 150), InnerRadius = 15, OuterRadius = 30, Slope = new Vec2d(0.012, 0.010) });
            foreach (List<Vec2d> bunker in new[] { bunkerFrontLeft, bunkerFrontRight, bunkerBackRight, bunkerPot })
                hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Bowl, InnerRadius = 1.4, Height = -0.5, Path = bunker });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Shelf, InnerRadius = 0, OuterRadius = 7, Height = -8.6, Path = cliff });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Shelf, InnerRadius = 0, OuterRadius = 24, Height = -12.5, Path = shore });

            hole.Surfaces.Add(new SurfaceRegion { Id = "deep-rough-right", Surface = SurfaceId.DeepRough, Priority = 10, Polygon = Shapes.Rect(31, -14, 45, 190) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "coastal-scrub", Surface = SurfaceId.DeepRough, Priority = 12, Polygon = InlandStrip(cliff, 4.0, -20, 196) });
            hole.Surfaces.Add(new SurfaceRegion
            {
                Id = "fairway",
                Surface = SurfaceId.Fairway,
                Priority = 20,
                Polygon = Shapes.CatmullRomLoop(new List<Vec2d>
                {
                    new Vec2d(-7, 64), new Vec2d(8, 62), new Vec2d(15, 84), new Vec2d(16, 108), new Vec2d(13, 128),
                    new Vec2d(3, 137), new Vec2d(-9, 137), new Vec2d(-17, 129), new Vec2d(-18, 112), new Vec2d(-14, 86),
                }, 6),
            });
            hole.Surfaces.Add(new SurfaceRegion { Id = "cart-path-1", Surface = SurfaceId.CartPath, Priority = 30, Polygon = Quad(21, -12, 23.5, -12, 24.5, 40, 22, 40) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "cart-path-2", Surface = SurfaceId.CartPath, Priority = 30, Polygon = Quad(22, 40, 24.5, 40, 26.5, 95, 24, 95) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "cart-path-3", Surface = SurfaceId.CartPath, Priority = 30, Polygon = Quad(24, 95, 26.5, 95, 25.5, 150, 23, 150) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "cart-path-4", Surface = SurfaceId.CartPath, Priority = 30, Polygon = Quad(23, 150, 25.5, 150, 21.5, 188, 19, 188) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "green", Surface = SurfaceId.Green, Priority = 40, Polygon = greenShape });
            hole.Surfaces.Add(new SurfaceRegion { Id = "bunker-front-left", Surface = SurfaceId.Bunker, Priority = 45, Polygon = bunkerFrontLeft });
            hole.Surfaces.Add(new SurfaceRegion { Id = "bunker-front-right", Surface = SurfaceId.Bunker, Priority = 45, Polygon = bunkerFrontRight });
            hole.Surfaces.Add(new SurfaceRegion { Id = "bunker-back-right", Surface = SurfaceId.Bunker, Priority = 45, Polygon = bunkerBackRight });
            hole.Surfaces.Add(new SurfaceRegion { Id = "bunker-pot-left", Surface = SurfaceId.Bunker, Priority = 45, Polygon = bunkerPot });
            hole.Surfaces.Add(new SurfaceRegion { Id = "tee", Surface = SurfaceId.Tee, Priority = 50, Polygon = Shapes.Rect(-5, -5, 5, 5) });

            var cove = new List<Vec2d>();
            foreach (Vec2d p in cliff)
            {
                if (p.z >= -20 && p.z <= 196) cove.Add(p);
            }
            cove.Add(new Vec2d(-130, 196));
            cove.Add(new Vec2d(-130, -20));
            hole.Hazards.Add(new HazardVolume { Id = "stillwater-cove", DropPointId = "drop-cove", Polygon = cove, WaterLevelM = SeaLevelM });
            hole.Drops.Add(new DropPoint { Id = "drop-cove", HazardId = "stillwater-cove", Position = new Vec2d(-12, 108) });

            hole.Trees.Add(new TreeObstacle { Id = "cypress-lone", Position = new Vec2d(-27.5, 30), TrunkRadius = 0.5, TrunkHeight = 3.5, CanopyRadius = 6.5, CanopyHeight = 9.5 });
            hole.Trees.Add(new TreeObstacle { Id = "cypress-cliff", Position = new Vec2d(-23.5, 106), TrunkRadius = 0.45, TrunkHeight = 3.2, CanopyRadius = 5.5, CanopyHeight = 8.5 });
            hole.Trees.Add(new TreeObstacle { Id = "cypress-tee-right", Position = new Vec2d(16, 14), TrunkRadius = 0.45, TrunkHeight = 3.5, CanopyRadius = 5, CanopyHeight = 9 });
            hole.Trees.Add(new TreeObstacle { Id = "cypress-right", Position = new Vec2d(28, 70), TrunkRadius = 0.5, TrunkHeight = 4, CanopyRadius = 6, CanopyHeight = 11 });
            hole.Trees.Add(new TreeObstacle { Id = "cypress-green-right", Position = new Vec2d(18, 121), TrunkRadius = 0.45, TrunkHeight = 3.5, CanopyRadius = 5.5, CanopyHeight = 10 });
            hole.Trees.Add(new TreeObstacle { Id = "cypress-back", Position = new Vec2d(9, 181), TrunkRadius = 0.55, TrunkHeight = 4, CanopyRadius = 6.5, CanopyHeight = 11 });
            hole.Trees.Add(new TreeObstacle { Id = "cypress-back-left", Position = new Vec2d(-19, 178), TrunkRadius = 0.45, TrunkHeight = 3.2, CanopyRadius = 5.5, CanopyHeight = 9 });

            hole.PlayableBoundary = Shapes.Rect(-110, -14, 45, 190);

            hole.TeeM = OnGround(hole, 0, 0);
            hole.CupM = OnGround(hole, -4, 151);
            hole.PlayRouteM.Add(hole.TeeM);
            hole.PlayRouteM.Add(hole.CupM);
            return hole;
        }

        private static List<Vec2d> Quad(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz) =>
            new List<Vec2d> { new Vec2d(ax, az), new Vec2d(bx, bz), new Vec2d(cx, cz), new Vec2d(dx, dz) };

        /// <summary>Band of the given width on the inland (right) side of a path, limited to a z range.</summary>
        private static List<Vec2d> InlandStrip(List<Vec2d> path, double width, double minZ, double maxZ)
        {
            var edge = new List<Vec2d>();
            var inner = new List<Vec2d>();
            for (int i = 0; i < path.Count; i++)
            {
                Vec2d p = path[i];
                if (p.z < minZ || p.z > maxZ) continue;
                Vec2d a = path[Math.Max(0, i - 1)], b = path[Math.Min(path.Count - 1, i + 1)];
                double tx = b.x - a.x, tz = b.z - a.z;
                double len = Math.Sqrt(tx * tx + tz * tz);
                edge.Add(p);
                inner.Add(new Vec2d(p.x + tz / len * width, p.z - tx / len * width));
            }
            inner.Reverse();
            edge.AddRange(inner);
            return edge;
        }

        private static Vec3d OnGround(HoleDefinition hole, double x, double z) =>
            new Vec3d(x, TerrainModel.Height(hole.Terrain, x, z), z);
    }
}
