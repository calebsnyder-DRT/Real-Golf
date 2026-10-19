using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;

namespace Puttshack.Tests
{
    /// <summary>The shared course frame: every hole validates, and the cart path links each green to the next tee.</summary>
    public sealed class CourseLayoutTests
    {
        [Test]
        public void Course_Validates()
        {
            CollectionAssert.IsEmpty(CourseValidator.Validate(GrayboxCourse.Create()));
        }

        [Test]
        public void TransitionRoutes_RunFromEachGreenToTheNextTee()
        {
            CourseDefinition course = GrayboxCourse.Create();
            for (int i = 1; i < course.Holes.Count; i++)
            {
                HoleDefinition from = course.Holes[i - 1], to = course.Holes[i];
                List<Vec2d> route = CourseLayout.TransitionRoute(from, to);
                double length = CourseLayout.Length(route);
                Assert.Greater(length, 20, $"{from.Id} -> {to.Id}");
                Assert.Less(length, 260, $"{from.Id} -> {to.Id}");
                Assert.Less(Distance(route[0], from.CupM), 45, "The drive starts by the green.");
                Assert.Less(Distance(route[route.Count - 1], to.TeeM), CourseLayout.TeeParkDistanceM + 1.6, "The drive ends by the tee.");
            }
        }

        [Test]
        public void TeeParking_IsOffTheTeeAndOutOfHazards()
        {
            foreach (HoleDefinition hole in GrayboxCourse.Create().Holes)
            {
                Vec2d park = CourseLayout.TeePark(hole);
                var world = new AnalyticWorld(hole);
                Assert.Greater(Distance(park, hole.TeeM), 3, hole.Id);
                Assert.IsNull(world.HazardAt(park.x, park.z), hole.Id);
                Assert.AreNotEqual(SurfaceId.Tee, world.SurfaceAt(park.x, park.z), hole.Id);
            }
        }

        private static double Distance(Vec2d a, Vec3d b) => System.Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
    }
}
