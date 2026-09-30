using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    public static class GrayboxCourse
    {
        public const string CourseId = "puttshack-pro-graybox";
        public const string CourseVersion = "graybox-0.1";

        /// <summary>Proposed par-72 layout (spec sections 26-27, Hole 1 corrected to par 4).</summary>
        public static readonly IReadOnlyList<int> ProposedPars = new[] { 4, 4, 3, 4, 5, 4, 3, 4, 5, 4, 3, 5, 4, 4, 3, 4, 5, 4 };

        public static CourseDefinition Create()
        {
            var course = new CourseDefinition
            {
                Id = CourseId,
                Version = CourseVersion,
                Name = "Puttshack Pro - Graybox",
            };
            course.Holes.Add(GardenGreen());
            ContentHasher.Seal(course);
            return course;
        }

        /// <summary>
        /// Hole 3 "Garden Green", par 3, 165 yd. One hole that exercises every rule: green with break, fairway,
        /// light and deep rough, two bunkers, a cart path, water with a drop point, tree trunks, and out-of-bounds.
        /// </summary>
        public static HoleDefinition GardenGreen()
        {
            var hole = new HoleDefinition
            {
                Id = "h03-garden-green",
                Version = "graybox-0.1",
                Name = "Garden Green",
                Number = 3,
                Par = 3,
                YardageYd = 165,
                DefaultSurface = SurfaceId.LightRough,
                WindMps = new Vec3d(1.4, 0, -0.9),
                Briefing = new HoleBriefing
                {
                    Hazard = "Water guards the left of the green; a bunker sits front right.",
                    SafePlan = "Aim at the centre of the green. Short and right leaves an easy chip.",
                    SuggestedClubId = "iron5",
                },
            };

            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Plateau, Center = new Vec2d(0, 0), InnerRadius = 5.5, OuterRadius = 12, Height = 0.5 });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Plateau, Center = new Vec2d(1, 151), InnerRadius = 16, OuterRadius = 32, Height = 1.2 });
            hole.Terrain.Add(new TerrainFeature { Kind = TerrainFeatureKind.Tilt, Center = new Vec2d(1, 151), InnerRadius = 16, OuterRadius = 32, Slope = new Vec2d(0.008, 0.012) });

            hole.Surfaces.Add(new SurfaceRegion { Id = "deep-rough-right", Surface = SurfaceId.DeepRough, Priority = 10, Polygon = Shapes.Rect(32, -14, 48, 186) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "deep-rough-left", Surface = SurfaceId.DeepRough, Priority = 10, Polygon = Shapes.Rect(-58, -14, -46, 186) });
            hole.Surfaces.Add(new SurfaceRegion
            {
                Id = "fairway",
                Surface = SurfaceId.Fairway,
                Priority = 20,
                Polygon = new List<Vec2d> { new Vec2d(-11, 78), new Vec2d(11, 78), new Vec2d(15, 128), new Vec2d(10, 140), new Vec2d(-9, 140), new Vec2d(-14, 128) },
            });
            hole.Surfaces.Add(new SurfaceRegion { Id = "cart-path", Surface = SurfaceId.CartPath, Priority = 30, Polygon = Shapes.Rect(24, 2, 26.5, 168) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "green", Surface = SurfaceId.Green, Priority = 40, Polygon = Shapes.Ellipse(1, 151, 13, 11, 48) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "bunker-front-right", Surface = SurfaceId.Bunker, Priority = 45, Polygon = Shapes.Ellipse(15.5, 141, 5.5, 3.4, 32, 0.5) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "bunker-back-left", Surface = SurfaceId.Bunker, Priority = 45, Polygon = Shapes.Ellipse(-9, 164, 5, 2.8, 32, -0.3) });
            hole.Surfaces.Add(new SurfaceRegion { Id = "tee", Surface = SurfaceId.Tee, Priority = 50, Polygon = Shapes.Rect(-5, -5, 5, 5) });

            hole.Hazards.Add(new HazardVolume
            {
                Id = "lake-left",
                DropPointId = "drop-lake-left",
                Polygon = new List<Vec2d>
                {
                    new Vec2d(-44, 106), new Vec2d(-21, 110), new Vec2d(-17, 128), new Vec2d(-19, 156),
                    new Vec2d(-30, 166), new Vec2d(-44, 160),
                },
            });
            hole.Drops.Add(new DropPoint { Id = "drop-lake-left", HazardId = "lake-left", Position = new Vec2d(-14, 118) });

            hole.Trees.Add(new TreeObstacle { Id = "oak-left", Position = new Vec2d(-9, 100), TrunkRadius = 0.35, TrunkHeight = 6, CanopyRadius = 3.4, CanopyHeight = 9 });
            hole.Trees.Add(new TreeObstacle { Id = "oak-right", Position = new Vec2d(10.5, 121), TrunkRadius = 0.4, TrunkHeight = 6.5, CanopyRadius = 3.8, CanopyHeight = 10 });
            hole.Trees.Add(new TreeObstacle { Id = "pine-tee-left", Position = new Vec2d(-22, 38), TrunkRadius = 0.3, TrunkHeight = 8, CanopyRadius = 2.6, CanopyHeight = 11 });
            hole.Trees.Add(new TreeObstacle { Id = "pine-right", Position = new Vec2d(28, 64), TrunkRadius = 0.3, TrunkHeight = 8, CanopyRadius = 2.6, CanopyHeight = 11 });
            hole.Trees.Add(new TreeObstacle { Id = "oak-back", Position = new Vec2d(-4, 178), TrunkRadius = 0.45, TrunkHeight = 6, CanopyRadius = 4.2, CanopyHeight = 10 });
            hole.Trees.Add(new TreeObstacle { Id = "oak-back-right", Position = new Vec2d(19, 172), TrunkRadius = 0.4, TrunkHeight = 6, CanopyRadius = 3.6, CanopyHeight = 9 });

            hole.PlayableBoundary = Shapes.Rect(-58, -14, 48, 186);

            hole.TeeM = OnGround(hole, 0, 0);
            hole.CupM = OnGround(hole, 3, 153);
            hole.PlayRouteM.Add(hole.TeeM);
            hole.PlayRouteM.Add(hole.CupM);
            return hole;
        }

        private static Vec3d OnGround(HoleDefinition hole, double x, double z) =>
            new Vec3d(x, TerrainModel.Height(hole.Terrain, x, z), z);
    }
}
