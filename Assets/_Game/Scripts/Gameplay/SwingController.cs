using System;
using RealGolf.Core;
using RealGolf.Data;
using UnityEngine;

namespace RealGolf.Gameplay
{
    public struct ShotParameters
    {
        public Vector3 Velocity;
        public float Spin;
        public float Power;
        public float AccuracyOffset;
        public Club Club;
    }

    /// <summary>
    /// Classic three-click swing: click to start the meter, click again to set
    /// power, then click a third time as the meter falls back through zero to
    /// set contact. Missing the mark hooks or slices the shot.
    /// </summary>
    public sealed class SwingController : MonoBehaviour
    {
        private const float PowerRiseSpeed = 1.05f;
        private const float AccuracyFallSpeed = 1.45f;
        private const float WorstAccuracy = -0.35f;
        private const float AimSpeedDegrees = 32f;
        private const float SpinSpeed = 1.6f;

        public event Action<ShotParameters> ShotTaken;

        public SwingStage Stage { get; private set; } = SwingStage.Ready;
        public float MeterValue { get; private set; }
        public float LockedPower { get; private set; }
        public float AimYaw { get; private set; }
        public int ClubIndex { get; private set; } = ClubSet.DriverIndex;
        public bool Active { get; private set; }

        /// <summary>
        /// Shot shaping from -1 (topspin, runs out) through 0 (stock) to +1
        /// (backspin, checks up). Set before the swing starts and held until the
        /// ball is struck.
        /// </summary>
        public float SpinBias { get; private set; }

        public Club CurrentClub => ClubSet.All[Mathf.Clamp(ClubIndex, 0, ClubSet.All.Count - 1)];

        private SurfaceType _lie = SurfaceType.TeeBox;
        private Vector3 _ballPosition;
        private float _cupDistance;

        /// <summary>
        /// Roll a full-power putt should produce. The putter's own MaxCarryMetres is
        /// a flat ~28 m, which made every short putt a sliver of meter travel: a 3 m
        /// putt needed 11% power. Scaling to the putt actually faced puts the useful
        /// range in the middle of the meter, with enough headroom to run one past.
        /// </summary>
        public float PuttFullPowerMetres => Mathf.Max(2.5f, _cupDistance * 1.35f);

        public Vector3 AimDirection => Quaternion.Euler(0f, AimYaw, 0f) * Vector3.forward;

        public void BeginTurn(GolfPlayer player, Vector3 ballPosition, Vector3 cupPosition, SurfaceType lie)
        {
            Active = true;
            Stage = SwingStage.Ready;
            MeterValue = 0f;
            LockedPower = 0f;
            SpinBias = 0f;
            _lie = lie;
            _ballPosition = ballPosition;

            Vector3 toCup = cupPosition - ballPosition;
            toCup.y = 0f;
            _cupDistance = toCup.magnitude;
            if (toCup.sqrMagnitude > 0.001f)
            {
                AimYaw = Quaternion.LookRotation(toCup).eulerAngles.y;
            }

            ClubIndex = ClubSet.SuggestClub(toCup.magnitude, lie);
            player.ClubIndex = ClubIndex;
        }

        public void Cancel()
        {
            Active = false;
            Stage = SwingStage.Ready;
            MeterValue = 0f;
        }

        public void Tick()
        {
            if (!Active)
            {
                return;
            }

            switch (Stage)
            {
                case SwingStage.Ready:
                    TickAiming();
                    break;

                case SwingStage.Power:
                    TickPower();
                    break;

                case SwingStage.Accuracy:
                    TickAccuracy();
                    break;
            }
        }

        private void TickAiming()
        {
            float aim = GolfInput.AimAxis;
            if (Mathf.Abs(aim) > 0.01f)
            {
                AimYaw += aim * AimSpeedDegrees * Time.deltaTime;
            }

            // The putter rolls the ball by definition, so shaping is off the table.
            if (!CurrentClub.IsPutter)
            {
                float shape = GolfInput.PitchAxis;
                if (Mathf.Abs(shape) > 0.01f)
                {
                    SpinBias = Mathf.Clamp(SpinBias + shape * SpinSpeed * Time.deltaTime, -1f, 1f);
                }
            }
            else
            {
                SpinBias = 0f;
            }

            if (GolfInput.ClubUpPressed)
            {
                ClubIndex = Mathf.Min(ClubSet.All.Count - 1, ClubIndex + 1);
            }

            if (GolfInput.ClubDownPressed)
            {
                ClubIndex = Mathf.Max(0, ClubIndex - 1);
            }

            if (GolfInput.SwingPressed)
            {
                Stage = SwingStage.Power;
                MeterValue = 0f;
            }
        }

        private void TickPower()
        {
            MeterValue += PowerRiseSpeed * Time.deltaTime;

            if (GolfInput.SwingPressed)
            {
                LockedPower = Mathf.Clamp01(MeterValue);
                Stage = SwingStage.Accuracy;
                return;
            }

            if (MeterValue >= 1f)
            {
                MeterValue = 1f;
                LockedPower = 1f;
                Stage = SwingStage.Accuracy;
            }
        }

        private void TickAccuracy()
        {
            MeterValue -= AccuracyFallSpeed * Time.deltaTime;

            if (GolfInput.SwingPressed)
            {
                Fire(MeterValue);
                return;
            }

            if (MeterValue <= WorstAccuracy)
            {
                Fire(WorstAccuracy);
            }
        }

        private void Fire(float accuracyOffset)
        {
            Stage = SwingStage.Complete;
            Active = false;

            Club club = CurrentClub;
            float power = Mathf.Clamp(LockedPower, 0.05f, 1f);

            float offlineDegrees = accuracyOffset * club.DispersionDegrees * 6f;
            float liePenalty = UnityEngine.Random.Range(-1f, 1f) * _lie.AccuracyPenaltyDegrees();
            float yaw = AimYaw + offlineDegrees + liePenalty;

            // Shaping is asymmetric: you can add a lot of backspin, but the ball can
            // only ever be rolled so far forward before it is simply a bad strike.
            float spin = club.Spin * power * (SpinBias >= 0f ? 1f + SpinBias * 0.8f : 1f + SpinBias * 1.4f);

            float loft;
            float speed;

            if (club.IsPutter)
            {
                loft = 0.8f;
                spin = 0f;
                float target = PuttFullPowerMetres * power;
                speed = BallisticSolver.SolvePuttSpeed(target, SurfaceType.Green.RollDrag());
            }
            else
            {
                // Backspin flights the ball higher and costs a little carry; topspin
                // does the reverse. Solving against the shaped spin keeps the carry
                // honest so club distances stay learnable.
                loft = club.LoftDegrees + SpinBias * 2.5f;
                float carry = club.MaxCarryMetres * power * _lie.PowerMultiplier()
                              * (1f - Mathf.Max(0f, SpinBias) * 0.08f);
                speed = BallisticSolver.SolveLaunchSpeed(loft, spin, carry);
            }

            Vector3 horizontal = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float loftRad = loft * Mathf.Deg2Rad;
            Vector3 velocity = (horizontal * Mathf.Cos(loftRad) + Vector3.up * Mathf.Sin(loftRad)) * speed;

            ShotTaken?.Invoke(new ShotParameters
            {
                Velocity = velocity,
                Spin = spin,
                Power = power,
                AccuracyOffset = accuracyOffset,
                Club = club
            });
        }

        /// <summary>Distance readout shown on the HUD for the club in hand.</summary>
        public float EstimatedDistance(float power)
        {
            Club club = CurrentClub;
            if (club.IsPutter)
            {
                return PuttFullPowerMetres * Mathf.Clamp01(power);
            }

            return club.MaxCarryMetres * Mathf.Clamp01(power) * _lie.PowerMultiplier();
        }

        public string AccuracyLabel(float offset)
        {
            float magnitude = Mathf.Abs(offset);
            if (magnitude < 0.02f)
            {
                return "Pure";
            }

            string side = offset > 0f ? "Push" : "Pull";
            return magnitude < 0.08f ? $"Slight {side}" : side;
        }

        /// <summary>Shot shaping described in words for the HUD.</summary>
        public string SpinLabel
        {
            get
            {
                if (CurrentClub.IsPutter)
                {
                    return "—";
                }

                return SpinBias switch
                {
                    > 0.55f => "Heavy Backspin",
                    > 0.08f => "Backspin",
                    < -0.55f => "Heavy Topspin",
                    < -0.08f => "Topspin",
                    _ => "Stock"
                };
            }
        }
    }
}
