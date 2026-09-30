using System;
using Puttshack.Content;
using Puttshack.Domain;

namespace Puttshack.Input
{
    /// <summary>Triangle-wave power meter (spec section 11). p = 1 - |u - 1| with u = (t / T) mod 2.</summary>
    public static class MeterWaveform
    {
        public static double Power(double elapsedMs, double sweepSeconds)
        {
            if (sweepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(sweepSeconds));
            if (elapsedMs < 0) return 0;
            double u = (elapsedMs / 1000.0 / sweepSeconds) % 2.0;
            return 1.0 - Math.Abs(u - 1.0);
        }

        public static double SweepSeconds(ClubDefinition club, AssistanceProfile profile) => club.SweepBaseSeconds * profile.SweepMultiplier;

        public static int DisplayPercent(double power01) => (int)Math.Round(power01 * 100, MidpointRounding.AwayFromZero);
    }

    /// <summary>Logical lane 1..20 selects a lateral angle inside the aim fan (spec section 12).</summary>
    public static class LaneMapper
    {
        public const int LaneCount = 20;

        public static bool IsValidLane(int lane) => lane >= 1 && lane <= LaneCount;

        public static double Q(int lane) => (lane - 10.5) / 9.5;

        public static double RawYawRad(int lane, double fanHalfAngleRad) => Q(lane) * fanHalfAngleRad;
    }

    /// <summary>Deterministic aim-error compression around the aim center. Never picks a landing after the fact.</summary>
    public static class Assistance
    {
        public static double ApplyYaw(double rawYawRad, double centerYawRad, AssistanceProfile profile)
        {
            double e = rawYawRad - centerYawRad;
            double d = Units.DegToRad(profile.DeadbandDeg);
            double corrected = Math.Sign(e) * Math.Max(0, Math.Abs(e) - d) * profile.Gain;
            return centerYawRad + corrected;
        }
    }
}
