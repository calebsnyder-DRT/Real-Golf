namespace Puttshack.Domain
{
    /// <summary>SI internally; convert only at presentation boundaries.</summary>
    public static class Units
    {
        public const double MetersPerYard = 0.9144;
        public const double MetersPerFoot = 0.3048;
        public const double MpsPerMph = 0.44704;
        public const double FeetThresholdYards = 30.0;

        public static double MetersToYards(double m) => m / MetersPerYard;
        public static double YardsToMeters(double yd) => yd * MetersPerYard;
        public static double MetersToFeet(double m) => m / MetersPerFoot;
        public static double FeetToMeters(double ft) => ft * MetersPerFoot;
        public static double MpsToMph(double mps) => mps / MpsPerMph;
        public static double DegToRad(double deg) => deg * System.Math.PI / 180.0;
        public static double RadToDeg(double rad) => rad * 180.0 / System.Math.PI;

        /// <summary>Remaining-distance label: feet inside 30 yards, yards beyond, always with the unit.</summary>
        public static string FormatDistance(double meters)
        {
            double yards = MetersToYards(meters);
            if (yards < FeetThresholdYards)
                return $"{MetersToFeet(meters):0} ft";
            return $"{yards:0} yd";
        }
    }
}
