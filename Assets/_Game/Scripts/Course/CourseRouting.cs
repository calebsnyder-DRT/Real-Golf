using System.Collections.Generic;
using RealGolf.Data;
using UnityEngine;

namespace RealGolf.Course
{
    /// <summary>Where a hole stands in the world, and which way it plays.</summary>
    public readonly struct HolePose
    {
        public readonly Vector3 Position;
        public readonly float Yaw;

        public HolePose(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }

        public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Hole-local planar point to world.</summary>
        public Vector3 ToWorld(Vector2 local) => Position + Rotation * new Vector3(local.x, 0f, local.y);
    }

    /// <summary>
    /// Packs the holes into one world. Each hole is authored in its own local space
    /// with the tee near the origin and play running +Z, which is all a single hole
    /// needs; to see the rest of the course at once they have to be given poses.
    ///
    /// The routing walks the course: each hole starts near the previous green, and
    /// the whole thing turns steadily so eighteen holes close back on the first tee
    /// the way a real routing does. Both the editor baker and the runtime aligner
    /// read this, so the baked scene and the played hole can never disagree.
    /// </summary>
    public static class CourseRouting
    {
        /// <summary>Walk from a green to the next tee.</summary>
        private const float TransferMetres = 42f;

        public static List<HolePose> Layout(CourseDefinition course)
        {
            var poses = new List<HolePose>(course.Holes.Count);
            var rng = new System.Random(course.Seed ^ 0x5f3a);

            // Turning through a full circle over the round keeps the course compact
            // and puts the closing hole back beside the opener.
            float turnPerHole = course.Holes.Count > 0 ? 360f / course.Holes.Count : 0f;

            Vector3 cursor = Vector3.zero;
            float yaw = 0f;

            foreach (HoleDefinition hole in course.Holes)
            {
                var pose = new HolePose(cursor, yaw);
                poses.Add(pose);

                // Step off from this green, turn, and walk to the next tee.
                Vector3 green = pose.ToWorld(hole.GreenCenter);
                yaw += turnPerHole + (float)(rng.NextDouble() * 16.0 - 8.0);
                cursor = green + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, TransferMetres);
            }

            return poses;
        }
    }
}
