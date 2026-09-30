using UnityEngine;

namespace RealGolf.Gameplay
{
    /// <summary>
    /// Wind for a single hole. The ball feels this through aerodynamic drag
    /// rather than a direct push, so a headwind both shortens and steepens a
    /// shot the way it does in reality, and the effect grows with ball speed.
    /// </summary>
    public sealed class Wind
    {
        private const float MetresPerSecondToMph = 2.23694f;

        /// <summary>Horizontal unit vector pointing the way the air is moving.</summary>
        public Vector3 Direction { get; private set; } = Vector3.forward;

        public float SpeedMetresPerSecond { get; private set; }

        public Vector3 Velocity => Direction * SpeedMetresPerSecond;

        public float SpeedMph => SpeedMetresPerSecond * MetresPerSecondToMph;

        public bool IsCalm => SpeedMetresPerSecond < 0.5f;

        /// <summary>
        /// Deterministic per-hole wind so the same seed always plays the same.
        /// Most holes get a light breeze; the occasional one gets teeth.
        /// </summary>
        public static Wind ForHole(int courseSeed, int holeNumber)
        {
            var rng = new System.Random(courseSeed * 397 + holeNumber * 7919);

            float bearing = (float)rng.NextDouble() * 360f;

            // Skew toward gentler winds, with a long tail for the rare gusty hole.
            double roll = rng.NextDouble();
            float speed = Mathf.Lerp(0f, 11f, (float)(roll * roll));

            return new Wind
            {
                Direction = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward,
                SpeedMetresPerSecond = speed
            };
        }

        public static Wind Calm() => new() { Direction = Vector3.forward, SpeedMetresPerSecond = 0f };

        /// <summary>
        /// Signed angle from the shot direction to the wind, where 0 means the
        /// wind blows the same way the ball is hit (a helping wind).
        /// </summary>
        public float RelativeAngle(Vector3 shotDirection)
        {
            var shot = new Vector3(shotDirection.x, 0f, shotDirection.z);
            if (shot.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(shot.normalized, Direction, Vector3.up);
        }

        /// <summary>Short readout such as "8 mph  into, L to R".</summary>
        public string Describe(Vector3 shotDirection)
        {
            if (IsCalm)
            {
                return "Calm";
            }

            float angle = RelativeAngle(shotDirection);
            float magnitude = Mathf.Abs(angle);

            string along = magnitude switch
            {
                < 40f => "helping",
                > 140f => "into",
                _ => null
            };

            // A wind blowing toward +90 degrees from the shot pushes the ball right to left.
            string across = magnitude is > 20f and < 160f
                ? (angle > 0f ? "R to L" : "L to R")
                : null;

            string qualifier = (along, across) switch
            {
                (null, null) => string.Empty,
                (null, _) => across,
                (_, null) => along,
                _ => $"{along}, {across}"
            };

            return string.IsNullOrEmpty(qualifier)
                ? $"{SpeedMph:0} mph"
                : $"{SpeedMph:0} mph  {qualifier}";
        }
    }
}
