using RealGolf.Course;
using UnityEngine;

namespace RealGolf.Gameplay
{
    /// <summary>
    /// What a player works out standing behind the ball: how much the green falls
    /// between here and the cup, and which way the putt will bend. The whole line
    /// is sampled rather than just the ball, because a putt is shaped by the
    /// ground it crosses and not by the patch it starts on.
    /// </summary>
    public readonly struct GreenRead
    {
        /// <summary>Average slope along the line. Positive runs downhill to the cup.</summary>
        public readonly float Along;

        /// <summary>Average slope across the line. Positive pushes the ball right.</summary>
        public readonly float Across;

        private const float Noticeable = 0.012f;
        private const float Strong = 0.038f;

        public GreenRead(float along, float across)
        {
            Along = along;
            Across = across;
        }

        public static GreenRead Sample(HoleTerrain terrain, Vector3 ball, Vector3 cup)
        {
            if (terrain == null)
            {
                return new GreenRead(0f, 0f);
            }

            Vector3 line = cup - ball;
            line.y = 0f;
            if (line.sqrMagnitude < 0.0001f)
            {
                return new GreenRead(0f, 0f);
            }

            Vector3 forward = line.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            const int Samples = 7;
            float along = 0f;
            float across = 0f;

            for (int i = 0; i < Samples; i++)
            {
                // Bias the samples toward the ball, where the putt still has the
                // speed to hold its line, rather than weighting the whole path evenly.
                float t = (i + 0.5f) / Samples;
                float weight = 1f - t * 0.5f;

                Vector3 point = ball + line * t;
                Vector3 normal = terrain.SurfaceNormal(new Vector2(point.x, point.z));
                if (normal.y < 0.0001f)
                {
                    continue;
                }

                // For a height field the horizontal part of the normal already points
                // downhill; dividing by the vertical part turns it into a gradient.
                var downhill = new Vector3(normal.x, 0f, normal.z) / normal.y;

                along += Vector3.Dot(downhill, forward) * weight;
                across += Vector3.Dot(downhill, right) * weight;
            }

            float total = 0f;
            for (int i = 0; i < Samples; i++)
            {
                total += 1f - ((i + 0.5f) / Samples) * 0.5f;
            }

            return new GreenRead(along / total, across / total);
        }

        /// <summary>Plain-language read for the HUD, e.g. "Downhill, breaks left".</summary>
        public string Describe()
        {
            string slope = Along switch
            {
                > Strong => "Downhill",
                > Noticeable => "Slightly downhill",
                < -Strong => "Uphill",
                < -Noticeable => "Slightly uphill",
                _ => "Flat"
            };

            string bend = Across switch
            {
                > Strong => "breaks hard right",
                > Noticeable => "breaks right",
                < -Strong => "breaks hard left",
                < -Noticeable => "breaks left",
                _ => null
            };

            return bend == null ? slope : $"{slope}, {bend}";
        }

        /// <summary>
        /// Direction the ball drifts, for the on-green arrow. Zero when the green
        /// is flat enough that drawing an arrow would be misleading.
        /// </summary>
        public Vector3 DownhillDirection(Vector3 forward, Vector3 right)
        {
            Vector3 drift = forward * Along + right * Across;
            return drift.sqrMagnitude < Noticeable * Noticeable ? Vector3.zero : drift.normalized;
        }
    }
}
