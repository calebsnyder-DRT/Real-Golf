using System.Collections.Generic;
using RealGolf.Course;
using UnityEngine;

namespace RealGolf.Data
{
    /// <summary>
    /// One selectable course. A course is nothing but a seed as far as the
    /// generator is concerned, so a preset is that seed plus the name and blurb to
    /// put on its card.
    /// </summary>
    public sealed class CoursePreset
    {
        public string Name { get; }
        public string Location { get; }
        public string Blurb { get; }
        public int Seed { get; }

        public CoursePreset(string name, string location, int seed, string blurb)
        {
            Name = name;
            Location = location;
            Seed = seed;
            Blurb = blurb;
        }
    }

    /// <summary>
    /// Everything a course card shows about a layout, measured by generating it
    /// rather than written down by hand. Nothing here can drift from what the
    /// player actually gets, because it *is* what the player gets.
    /// </summary>
    public readonly struct CourseStats
    {
        public readonly int Holes;
        public readonly int Par;
        public readonly float TotalMetres;
        public readonly float LongestHoleMetres;
        public readonly int Bunkers;
        public readonly int WaterHazards;

        /// <summary>1-5, derived from the measured length and hazard count.</summary>
        public readonly int Difficulty;

        public CourseStats(
            int holes, int par, float totalMetres, float longestHoleMetres,
            int bunkers, int waterHazards, int difficulty)
        {
            Holes = holes;
            Par = par;
            TotalMetres = totalMetres;
            LongestHoleMetres = longestHoleMetres;
            Bunkers = bunkers;
            WaterHazards = waterHazards;
            Difficulty = difficulty;
        }
    }

    /// <summary>
    /// The courses offered on the menu.
    ///
    /// The seeds are arbitrary fixed numbers. They are not tuned to produce any
    /// particular layout, and they cannot be: <see cref="CourseGenerator"/> draws
    /// every length and hazard from the seed, so the only way to know what a seed
    /// gives is to generate it. That is exactly what <see cref="StatsFor"/> does,
    /// which is why the cards can be trusted -- they report the real layout.
    ///
    /// The consequence is that the courses differ less than their names suggest.
    /// Par is always 72 because the generator uses one fixed par pattern, and the
    /// length envelope is only 5280-7020 m wide, so two seeds typically land within
    /// a few hundred metres of each other. CourseLibraryVerify prints the measured
    /// spread; if these six bunch up, swap seeds until they do not.
    /// </summary>
    public static class CourseLibrary
    {
        /// <summary>
        /// Total length the generator can produce, summed over its own per-hole
        /// draws: 4 par 3s at 120-190 m, 10 par 4s at 300-410 m and 4 par 5s at
        /// 450-540 m. The centreline partitions each hole's draw exactly, doglegs
        /// included, so these bounds are hard rather than approximate.
        /// </summary>
        private const float ShortestPossibleCourse = 5280f;
        private const float LongestPossibleCourse = 7020f;

        /// <summary>
        /// Hazard envelope. Every hole gets 2-3 greenside bunkers (36-54), the 14
        /// non-par-3s get another 1-3 on the fairway (14-42), and water appears on
        /// 40% of those and 35% of the par 3s (0-18 in the worst case).
        /// </summary>
        private const int FewestPossibleHazards = 50;
        private const int MostPossibleHazards = 114;

        /// <summary>
        /// Length leads difficulty, but only just: a long course is tiring while
        /// water is where strokes are actually lost, so the two are weighted close
        /// to evenly.
        /// </summary>
        private const float LengthWeight = 0.55f;
        private const float HazardWeight = 0.45f;

        private static readonly List<CoursePreset> PresetList = new()
        {
            new CoursePreset(
                "Cypress Hollow", "Monterey Peninsula", 10_432,
                "Cliff-top parkland where the wind decides the club."),
            new CoursePreset(
                "Brackenmoor Links", "Fife Coast", 774_219,
                "Traditional links. Low, running shots and revetted sand."),
            new CoursePreset(
                "Alderfield Park", "Surrey Heath", 51_884,
                "Tight heathland corridors lined with pine and heather."),
            new CoursePreset(
                "Harrow Creek", "Blue Ridge Foothills", 320_057,
                "A creek crosses often enough to make the card nervous."),
            new CoursePreset(
                "Saltmarsh Point", "Cape Fear", 6_611_903,
                "Flat, exposed and long. Nowhere to hide from the breeze."),
            new CoursePreset(
                "Thornbury Old", "Cotswold Edge", 98_146,
                "The oldest layout here: short, awkward and full of doglegs."),
        };

        public static IReadOnlyList<CoursePreset> Presets => PresetList;

        /// <summary>
        /// Measured stats are cached per seed. Generating an 18-hole layout runs the
        /// tree scatter 900 times a hole, so it is a few milliseconds rather than
        /// free, and the menu asks for every course the moment it opens.
        /// </summary>
        private static readonly Dictionary<int, CourseStats> StatsCache = new();

        /// <summary>
        /// Generates the layout to measure it, then throws the layout away. The
        /// definition is deliberately not cached and handed to the game: a shared
        /// CourseDefinition would be mutable state that the round could write to,
        /// and regenerating from the same seed costs milliseconds and is always
        /// identical anyway.
        /// </summary>
        public static CourseStats StatsFor(CoursePreset preset)
        {
            if (preset == null)
            {
                return default;
            }

            if (StatsCache.TryGetValue(preset.Seed, out CourseStats cached))
            {
                return cached;
            }

            CourseDefinition course = CourseGenerator.Generate(preset.Seed, preset.Name);

            float total = 0f;
            float longest = 0f;
            int bunkers = 0;
            int water = 0;

            foreach (HoleDefinition hole in course.Holes)
            {
                float length = hole.LengthMetres;
                total += length;
                longest = Mathf.Max(longest, length);
                bunkers += hole.Bunkers.Count;
                water += hole.Water.Count;
            }

            var stats = new CourseStats(
                course.Holes.Count, course.TotalPar, total, longest, bunkers, water,
                RateDifficulty(total, bunkers + water));

            StatsCache[preset.Seed] = stats;
            return stats;
        }

        /// <summary>
        /// Places the course inside the generator's own envelope and maps that to
        /// 1-5. Rating against the envelope rather than against the other presets
        /// keeps a course's stars stable when the preset list is edited.
        /// </summary>
        private static int RateDifficulty(float totalMetres, int hazards)
        {
            float length = Mathf.InverseLerp(ShortestPossibleCourse, LongestPossibleCourse, totalMetres);
            float hazard = Mathf.InverseLerp(FewestPossibleHazards, MostPossibleHazards, hazards);
            float score = length * LengthWeight + hazard * HazardWeight;
            return Mathf.Clamp(1 + Mathf.RoundToInt(score * 4f), 1, 5);
        }
    }
}
