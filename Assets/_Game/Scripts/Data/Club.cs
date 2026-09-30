using System.Collections.Generic;

namespace RealGolf.Data
{
    public sealed class Club
    {
        public string Name { get; }
        public string ShortName { get; }

        /// <summary>Launch angle in degrees for a cleanly struck full shot.</summary>
        public float LoftDegrees { get; }

        /// <summary>Carry distance in metres at 100% power on a flat, calm hole.</summary>
        public float MaxCarryMetres { get; }

        /// <summary>Dispersion in degrees at the extremes of the accuracy meter.</summary>
        public float DispersionDegrees { get; }

        /// <summary>Backspin scalar; higher values stop the ball faster on the green.</summary>
        public float Spin { get; }

        public bool IsPutter { get; }

        public Club(string name, string shortName, float loft, float carry, float dispersion, float spin, bool isPutter = false)
        {
            Name = name;
            ShortName = shortName;
            LoftDegrees = loft;
            MaxCarryMetres = carry;
            DispersionDegrees = dispersion;
            Spin = spin;
            IsPutter = isPutter;
        }
    }

    /// <summary>
    /// The bag every player carries. Ordered longest to shortest so club up/down
    /// maps directly onto index movement.
    /// </summary>
    public static class ClubSet
    {
        public static readonly IReadOnlyList<Club> All = new List<Club>
        {
            new Club("Driver",         "DR",  10.5f, 230f, 3.2f, 0.25f),
            new Club("3 Wood",         "3W",  15f,   205f, 2.8f, 0.35f),
            new Club("5 Iron",         "5I",  27f,   165f, 2.2f, 0.55f),
            new Club("7 Iron",         "7I",  34f,   140f, 1.9f, 0.7f),
            new Club("9 Iron",         "9I",  42f,   115f, 1.7f, 0.85f),
            new Club("Pitching Wedge", "PW",  48f,    95f, 1.5f, 1.0f),
            new Club("Sand Wedge",     "SW",  56f,    65f, 1.4f, 1.2f),
            new Club("Putter",         "PT",   1f,    28f, 0.6f, 0f, isPutter: true)
        };

        public static int DriverIndex => 0;
        public static int PutterIndex => All.Count - 1;

        /// <summary>
        /// Picks the club whose stock distance best covers the shot, so a player
        /// teeing off or lagging a putt starts with something sensible in hand.
        /// </summary>
        public static int SuggestClub(float distanceToHole, SurfaceType lie)
        {
            if (lie == SurfaceType.Green)
            {
                return PutterIndex;
            }

            if (lie == SurfaceType.Bunker)
            {
                return All.Count - 2;
            }

            // Walk shortest to longest (skipping the putter) and take the first
            // club that covers the distance.
            for (int i = All.Count - 2; i >= 0; i--)
            {
                if (All[i].MaxCarryMetres >= distanceToHole)
                {
                    return i;
                }
            }

            return DriverIndex;
        }
    }
}
