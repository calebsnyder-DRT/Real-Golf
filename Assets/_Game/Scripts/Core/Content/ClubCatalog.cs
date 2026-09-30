using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    public sealed class ClubDefinition
    {
        public string Id;
        public string DisplayName;
        public ShotType ShotType;
        /// <summary>Launch speed (m/s) at power 0, .25, .5, .75, 1. Monotonic; zero power is zero speed.</summary>
        public double[] SpeedSamples;
        public double LaunchAngleDeg;
        /// <summary>Lift acceleration coefficient standing in for backspin (1/m). Declared, not inferred.</summary>
        public double LiftCoefficient;
        public double SweepBaseSeconds;
        public double FanHalfAngleDeg;
        public int RangeMinYd;
        public int RangeMaxYd;
        public bool RangeFromReference;
        /// <summary>Replaces the bunker's launch multiplier for clubs designed to escape sand.</summary>
        public double? BunkerLaunchMultiplier;
        public string Tooltip;

        public string RangeLabel => ShotType == ShotType.Putt ? "Feet scale" : $"{RangeMinYd}-{RangeMaxYd} yd";
    }

    /// <summary>Nine-club bag available to every player (spec section 17). Speeds fitted on the flat no-wind fixture.</summary>
    public static class ClubCatalog
    {
        public const string Version = "clubs-1.0";
        public const string PutterId = "putter";

        /// <summary>Maximum flat-green roll distance per putt scale (TUNE: 15, 40, 90 feet).</summary>
        public static double PuttScaleMaxMeters(PuttScale scale)
        {
            switch (scale)
            {
                case PuttScale.Short: return Units.FeetToMeters(15);
                case PuttScale.Long: return Units.FeetToMeters(90);
                default: return Units.FeetToMeters(40);
            }
        }

        public static readonly IReadOnlyList<ClubDefinition> All = new List<ClubDefinition>
        {
            new ClubDefinition { Id = "driver", DisplayName = "Driver", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 40.97, 54.80, 67.67, 82.09 }, LaunchAngleDeg = 11, LiftCoefficient = 0.0030, SweepBaseSeconds = 1.6, FanHalfAngleDeg = 12, RangeMinYd = 200, RangeMaxYd = 300, RangeFromReference = true, Tooltip = "Longest stroke. Low launch, fastest meter. Best from the tee." },
            new ClubDefinition { Id = "wood3", DisplayName = "3 Wood", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 35.52, 47.49, 57.78, 68.28 }, LaunchAngleDeg = 13, LiftCoefficient = 0.0030, SweepBaseSeconds = 1.8, FanHalfAngleDeg = 11, RangeMinYd = 180, RangeMaxYd = 240, Tooltip = "Safer long stroke with a slightly slower meter." },
            new ClubDefinition { Id = "iron5", DisplayName = "5 Iron", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 30.25, 41.05, 50.07, 58.82 }, LaunchAngleDeg = 17, LiftCoefficient = 0.0028, SweepBaseSeconds = 2.0, FanHalfAngleDeg = 10, RangeMinYd = 150, RangeMaxYd = 200, RangeFromReference = true, Tooltip = "Balanced long approach." },
            new ClubDefinition { Id = "iron7", DisplayName = "7 Iron", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 26.00, 35.59, 43.48, 50.90 }, LaunchAngleDeg = 21, LiftCoefficient = 0.0026, SweepBaseSeconds = 2.2, FanHalfAngleDeg = 9, RangeMinYd = 120, RangeMaxYd = 170, Tooltip = "Controlled approach. Higher, less roll than the 5 iron." },
            new ClubDefinition { Id = "iron9", DisplayName = "9 Iron", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 21.83, 30.10, 36.88, 43.13 }, LaunchAngleDeg = 27, LiftCoefficient = 0.0024, SweepBaseSeconds = 2.4, FanHalfAngleDeg = 8, RangeMinYd = 100, RangeMaxYd = 140, RangeFromReference = true, Tooltip = "Short approach with a forgiving meter." },
            new ClubDefinition { Id = "pw", DisplayName = "Pitching Wedge", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 18.54, 25.58, 31.27, 36.41 }, LaunchAngleDeg = 32, LiftCoefficient = 0.0022, SweepBaseSeconds = 2.5, FanHalfAngleDeg = 8, RangeMinYd = 60, RangeMaxYd = 110, Tooltip = "High approach, small rollout." },
            new ClubDefinition { Id = "sw", DisplayName = "Sand Wedge", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 15.29, 21.09, 25.73, 29.86 }, LaunchAngleDeg = 40, LiftCoefficient = 0.0018, SweepBaseSeconds = 2.5, FanHalfAngleDeg = 7, RangeMinYd = 20, RangeMaxYd = 80, BunkerLaunchMultiplier = 0.90, Tooltip = "Bunker recovery and short high shots." },
            new ClubDefinition { Id = "lw", DisplayName = "Lob Wedge", ShotType = ShotType.Full, SpeedSamples = new[] { 0, 11.81, 16.15, 19.54, 22.47 }, LaunchAngleDeg = 46, LiftCoefficient = 0.0016, SweepBaseSeconds = 2.5, FanHalfAngleDeg = 7, RangeMinYd = 10, RangeMaxYd = 50, Tooltip = "Short, high, minimal rollout." },
            new ClubDefinition { Id = PutterId, DisplayName = "Putter", ShotType = ShotType.Putt, SpeedSamples = new double[] { 0, 0, 0, 0, 0 }, LaunchAngleDeg = 0, LiftCoefficient = 0, SweepBaseSeconds = 2.8, FanHalfAngleDeg = 4, RangeMinYd = 0, RangeMaxYd = 30, Tooltip = "Ground stroke. Power sets roll speed; slope sets break." },
        };

        public static ClubDefinition Get(string id)
        {
            foreach (ClubDefinition club in All)
            {
                if (club.Id == id) return club;
            }
            throw new ArgumentException($"Unknown club '{id}'.", nameof(id));
        }

        public static bool TryGet(string id, out ClubDefinition club)
        {
            foreach (ClubDefinition c in All)
            {
                if (c.Id == id)
                {
                    club = c;
                    return true;
                }
            }
            club = null;
            return false;
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>Monotonic piecewise-linear interpolation across the five power samples.</summary>
        public static double LaunchSpeed(ClubDefinition club, double power01)
        {
            double p = Math.Max(0, Math.Min(1, power01));
            double scaled = p * 4.0;
            int i = Math.Min(3, (int)Math.Floor(scaled));
            double f = scaled - i;
            return club.SpeedSamples[i] + (club.SpeedSamples[i + 1] - club.SpeedSamples[i]) * f;
        }

        /// <summary>Initial putt speed so a flat green roll covers power * scale distance: v = sqrt(2 a d).</summary>
        public static double PuttSpeed(double power01, PuttScale scale, double rollingResistance)
        {
            double p = Math.Max(0, Math.Min(1, power01));
            return Math.Sqrt(2.0 * rollingResistance * p * PuttScaleMaxMeters(scale));
        }

        /// <summary>Suggests a club; never restricts the bag.</summary>
        public static string Suggest(double remainingM, SurfaceId lie, bool onTee)
        {
            if (lie == SurfaceId.Green) return PutterId;
            if (lie == SurfaceId.Bunker) return "sw";
            double yards = Units.MetersToYards(remainingM);
            if (yards < 12) return PutterId;
            if (onTee && yards > 250) return "driver";

            string best = "lw";
            foreach (ClubDefinition club in All)
            {
                if (club.ShotType == ShotType.Putt) continue;
                if (yards <= club.RangeMaxYd * 0.95) best = club.Id;
            }
            if (!onTee && best == "driver") best = "wood3";
            return best;
        }
    }

    public sealed class AssistanceProfile
    {
        public AssistanceProfileId Id;
        public string Label;
        public double SweepMultiplier;
        public double DeadbandDeg;
        public double Gain;
    }

    /// <summary>Deterministic forgiveness only: meter speed and aim-error compression (spec section 18).</summary>
    public static class AssistanceProfiles
    {
        public static AssistanceProfile Get(AssistanceProfileId id)
        {
            switch (id)
            {
                case AssistanceProfileId.Amateur:
                    return new AssistanceProfile { Id = id, Label = "AMATEUR", SweepMultiplier = 1.25, DeadbandDeg = 1.5, Gain = 0.70 };
                case AssistanceProfileId.Pro:
                    return new AssistanceProfile { Id = id, Label = "PRO", SweepMultiplier = 0.85, DeadbandDeg = 0.0, Gain = 1.00 };
                default:
                    return new AssistanceProfile { Id = AssistanceProfileId.Club, Label = "CLUB", SweepMultiplier = 1.00, DeadbandDeg = 0.6, Gain = 0.85 };
            }
        }
    }
}
