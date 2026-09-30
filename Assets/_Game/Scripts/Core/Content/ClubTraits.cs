using System;

namespace Puttshack.Content
{
    /// <summary>
    /// Pro Shop read-outs for a club, in gameplay terminology (brief: power, meter speed, accuracy width, forgiveness,
    /// wind sensitivity). Each value is 0..1 and derived from the <see cref="ClubDefinition"/> the solver uses, so the
    /// shop can only describe a club, never change one.
    /// </summary>
    public sealed class ClubTraits
    {
        public double Power;
        public double MeterSpeed;
        public double AccuracyWidth;
        public double Forgiveness;
        public double WindSensitivity;
        public string Summary;

        public static readonly string[] Labels = { "POWER", "METER SPEED", "ACCURACY WIDTH", "FORGIVENESS", "WIND SENSITIVITY" };

        public double this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return Power;
                    case 1: return MeterSpeed;
                    case 2: return AccuracyWidth;
                    case 3: return Forgiveness;
                    default: return WindSensitivity;
                }
            }
        }

        public static ClubTraits Of(ClubDefinition club)
        {
            const double fastestSweep = 1.4, slowestSweep = 3.0;
            double meter = Clamp01((slowestSweep - club.SweepBaseSeconds) / (slowestSweep - fastestSweep));
            double accuracy = Clamp01(1 - (club.FanHalfAngleDeg - 4) / 10.0);
            bool putt = club.ShotType == Domain.ShotType.Putt;
            return new ClubTraits
            {
                Power = putt ? 0.1 : Clamp01(club.RangeMaxYd / 300.0),
                MeterSpeed = meter,
                AccuracyWidth = accuracy,
                Forgiveness = Clamp01(0.15 + 0.5 * accuracy + 0.45 * (1 - meter)),
                WindSensitivity = putt ? 0 : Clamp01(0.2 + club.LaunchAngleDeg / 60.0),
                Summary = SummaryFor(club.Id),
            };
        }

        private static string SummaryFor(string id)
        {
            switch (id)
            {
                case "driver": return "MAX DISTANCE \u00b7 HIGH RISK";
                case "wood3": return "LONG \u00b7 MEDIUM RISK";
                case "iron5": return "BALANCED \u00b7 MEDIUM RISK";
                case "iron7": return "CONTROL \u00b7 LOW RISK";
                case "iron9": return "CONTROL \u00b7 LOW RISK";
                case "pw": return "APPROACH \u00b7 LOW RISK";
                case "sw": return "RECOVERY \u00b7 LOW RISK";
                case "lw": return "TOUCH \u00b7 MEDIUM RISK";
                case ClubCatalog.PutterId: return "ROLL \u00b7 FEET SCALE";
                default: return string.Empty;
            }
        }

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
    }
}
