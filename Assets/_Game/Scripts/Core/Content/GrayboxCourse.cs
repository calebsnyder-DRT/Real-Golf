using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>
    /// The authored Pebble Beach stretch, loaded from Assets/_Game/Resources/Course/Pebble (<see cref="CourseData"/>): one
    /// course.json for the shared world frame and one JSON file per hole. Every hole shares one world frame and one
    /// heightfield (<see cref="CourseTerrain"/>), so it sees its neighbours and the cart paths (<see cref="CourseLayout"/>)
    /// run continuously between them. Adding a hole means adding its file and listing it in course.json (Holes, Routing).
    /// </summary>
    public static class GrayboxCourse
    {
        private static CourseSpec Spec => CourseData.Load();

        public static string CourseId => Spec.Id;
        public static string CourseVersion => Spec.Version;

        /// <summary>Proposed par-72 layout (spec sections 26-27, Hole 1 corrected to par 4).</summary>
        public static IReadOnlyList<int> ProposedPars => Spec.ProposedPars;

        /// <summary>Sea surface in course metres (the clifftop near hole 3's green sits about 11 m above it).</summary>
        public static double SeaLevelM => Spec.SeaLevelM;

        public static CourseDefinition Create() => Spec.Build();

        /// <summary>One authored hole by id (unsealed).</summary>
        public static HoleDefinition Hole(string id) => Spec.BuildHole(id);

        /// <summary>Cliff top edge along the coast (the sea is on its left), from the south coast round the point and north past holes 5, 3 and 4.</summary>
        public static IReadOnlyList<Vec2d> CliffEdgeControl => Spec.PathControl("CliffEdge");

        /// <summary>Foot of the beach: past this line the sea floor falls away (narrow and rocky under the greens).</summary>
        public static IReadOnlyList<Vec2d> ShoreControl => Spec.PathControl("Shore");

        /// <summary>
        /// The cliff edge as the terrain samples it (a fresh copy). The path is built from pieces sampled separately, so
        /// prefer this to re-sampling <see cref="CliffEdgeControl"/>, which differs slightly where two pieces meet.
        /// </summary>
        public static List<Vec2d> CliffEdge => Spec.Path("CliffEdge");

        /// <summary>The shore line as the terrain samples it (a fresh copy), see <see cref="CliffEdge"/>.</summary>
        public static List<Vec2d> Shore => Spec.Path("Shore");

        /// <summary>One heightfield for the whole course; every hole carries a copy as its <see cref="HoleDefinition.Terrain"/>.</summary>
        public static List<TerrainFeature> CourseTerrain() => Spec.Terrain();
    }
}
