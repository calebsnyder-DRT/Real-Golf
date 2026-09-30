using RealGolf.Data;
using UnityEditor;
using UnityEngine;

namespace RealGolf.EditorTools
{
    /// <summary>
    /// Prints what the course presets actually generate.
    ///
    /// The preset seeds in <see cref="CourseLibrary"/> are arbitrary, so nobody
    /// knows what layout any of them produces until it is generated. The menu shows
    /// measured numbers and is therefore always honest, but honest is not the same
    /// as interesting: if every seed lands on 6100 m with 60 hazards, the player is
    /// choosing between six identical courses with different names.
    ///
    /// This measures the spread so that can be seen rather than assumed. Run it
    /// after editing the preset list.
    /// </summary>
    public static class CourseLibraryVerify
    {
        /// <summary>
        /// Below this, two courses are the same round as far as the player can tell.
        /// A hole is 300-400 m, so a spread narrower than one hole means the longest
        /// and shortest courses differ by less than a single par 4.
        /// </summary>
        private const float InterestingLengthSpread = 300f;

        /// <summary>
        /// Difficulty is shown as 1-5 stars. If every preset rates the same, the
        /// stars are decoration rather than information.
        /// </summary>
        private const int MinimumDistinctRatings = 2;

        [MenuItem("Real Golf/Verify Course Library", false, 20)]
        public static void Run()
        {
            Debug.Log("COURSES-BEGIN");

            float shortest = float.MaxValue;
            float longest = float.MinValue;
            int ratingMask = 0;
            int distinctRatings = 0;

            foreach (CoursePreset preset in CourseLibrary.Presets)
            {
                CourseStats stats = CourseLibrary.StatsFor(preset);

                Debug.Log(
                    $"COURSE {preset.Name,-20} seed={preset.Seed,-10} " +
                    $"par={stats.Par} {stats.TotalMetres,6:F0} m  " +
                    $"longest={stats.LongestHoleMetres,3:F0} m  " +
                    $"bunkers={stats.Bunkers,3}  water={stats.WaterHazards,2}  " +
                    $"difficulty={stats.Difficulty}/5");

                shortest = Mathf.Min(shortest, stats.TotalMetres);
                longest = Mathf.Max(longest, stats.TotalMetres);

                int bit = 1 << stats.Difficulty;
                if ((ratingMask & bit) == 0)
                {
                    ratingMask |= bit;
                    distinctRatings++;
                }
            }

            float spread = longest - shortest;
            Debug.Log($"COURSES spread={spread:F0} m ({shortest:F0}-{longest:F0}) " +
                      $"distinctRatings={distinctRatings}");

            if (spread < InterestingLengthSpread)
            {
                Debug.LogWarning(
                    $"[CourseLibraryVerify] The presets only span {spread:F0} m, under the " +
                    $"{InterestingLengthSpread:F0} m that makes one course feel different from " +
                    "another. Swap some seeds in CourseLibrary.PresetList and run this again.");
            }

            if (distinctRatings < MinimumDistinctRatings)
            {
                Debug.LogWarning(
                    "[CourseLibraryVerify] Every preset rates the same difficulty, so the stars " +
                    "on the menu tell the player nothing. Swap some seeds, or retune the weights " +
                    "in CourseLibrary.RateDifficulty.");
            }

            Debug.Log("COURSES-END");
        }
    }
}
