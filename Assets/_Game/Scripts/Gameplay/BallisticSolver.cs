using UnityEngine;

namespace RealGolf.Gameplay
{
    /// <summary>
    /// Finds the launch speed that makes a club actually carry its advertised
    /// distance. Rather than trusting a closed-form range equation (which ignores
    /// the drag and lift the ball really experiences), this integrates the same
    /// forces <see cref="GolfBall"/> applies and binary-searches the speed.
    /// </summary>
    public static class BallisticSolver
    {
        private const float Gravity = 9.81f;
        private const float StepSeconds = 0.02f;
        private const int MaxSteps = 2000;
        private const int SearchIterations = 26;

        public static float SolveLaunchSpeed(float loftDegrees, float spin, float targetCarry)
        {
            if (targetCarry <= 0.01f)
            {
                return 0f;
            }

            float low = 0.5f;
            float high = 130f;

            for (int i = 0; i < SearchIterations; i++)
            {
                float mid = (low + high) * 0.5f;
                if (SimulateCarry(mid, loftDegrees, spin) < targetCarry)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return (low + high) * 0.5f;
        }

        /// <summary>Carry distance for a launch, simulated in the vertical plane.</summary>
        public static float SimulateCarry(float speed, float loftDegrees, float spin)
        {
            float rad = loftDegrees * Mathf.Deg2Rad;
            var position = new Vector2(0f, 0.03f);
            var velocity = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * speed;
            float currentSpin = spin;

            for (int i = 0; i < MaxSteps; i++)
            {
                float v = velocity.magnitude;
                var acceleration = new Vector2(0f, -Gravity);

                if (v > 0.01f)
                {
                    Vector2 direction = velocity / v;
                    acceleration += -direction * (GolfBall.DragCoefficient * v * v);

                    // Lift acts 90 degrees off the direction of travel.
                    var lift = new Vector2(-direction.y, direction.x);
                    acceleration += lift * (GolfBall.LiftCoefficient * currentSpin * v * v);
                }

                velocity += acceleration * StepSeconds;
                Vector2 next = position + velocity * StepSeconds;
                currentSpin = Mathf.Max(0f, currentSpin - GolfBall.SpinDecay * StepSeconds);

                if (next.y <= 0f)
                {
                    // Interpolate to the exact ground crossing.
                    float t = position.y / Mathf.Max(0.0001f, position.y - next.y);
                    return Mathf.Lerp(position.x, next.x, t);
                }

                position = next;
            }

            return position.x;
        }

        /// <summary>
        /// Speed needed to roll a putt a given distance, derived from the constant
        /// rolling deceleration the ball applies on that surface.
        /// </summary>
        public static float SolvePuttSpeed(float distance, float rollDeceleration)
        {
            return Mathf.Sqrt(Mathf.Max(0.01f, 2f * rollDeceleration * distance));
        }
    }
}
