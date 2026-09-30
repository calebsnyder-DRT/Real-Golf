using System.Collections.Generic;
using UnityEngine;

namespace RealGolf.Data
{
    public struct CircleRegion
    {
        public Vector2 Center;
        public float Radius;

        public CircleRegion(Vector2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public bool Contains(Vector2 point) => (point - Center).sqrMagnitude <= Radius * Radius;
    }

    /// <summary>
    /// A single hole laid out in its own local space: the tee sits near the
    /// origin and play runs down +Z. Only the hole being played is ever built,
    /// so holes never need to be packed into a shared world grid.
    /// </summary>
    public sealed class HoleDefinition
    {
        public int Number;
        public int Par;
        public int Seed;

        /// <summary>Centreline from tee to green, including any dogleg corners.</summary>
        public List<Vector2> Centerline = new();

        public Vector2 TeePosition;
        public Vector2 GreenCenter;
        public float GreenRadius;
        public Vector2 CupPosition;

        public float FairwayWidth;

        public List<CircleRegion> Bunkers = new();
        public List<CircleRegion> Water = new();
        public List<Vector2> Trees = new();

        /// <summary>Planar bounds of the generated terrain plot.</summary>
        public Rect PlotBounds;

        public float LengthMetres
        {
            get
            {
                float total = 0f;
                for (int i = 1; i < Centerline.Count; i++)
                {
                    total += Vector2.Distance(Centerline[i - 1], Centerline[i]);
                }

                return total;
            }
        }

        public string ParLabel => Par switch
        {
            3 => "Par 3",
            4 => "Par 4",
            5 => "Par 5",
            _ => $"Par {Par}"
        };

        /// <summary>Shortest distance from a point to the playing corridor.</summary>
        public float DistanceToCenterline(Vector2 point)
        {
            float best = float.MaxValue;
            for (int i = 1; i < Centerline.Count; i++)
            {
                float d = DistanceToSegment(point, Centerline[i - 1], Centerline[i]);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-5f)
            {
                return Vector2.Distance(p, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }
    }

    public sealed class CourseDefinition
    {
        public string Name = "Real Golf Country Club";
        public int Seed;
        public List<HoleDefinition> Holes = new();

        public int TotalPar
        {
            get
            {
                int par = 0;
                foreach (HoleDefinition hole in Holes)
                {
                    par += hole.Par;
                }

                return par;
            }
        }
    }
}
