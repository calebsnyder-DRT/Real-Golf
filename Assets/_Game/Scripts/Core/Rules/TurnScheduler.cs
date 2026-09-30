using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Rules
{
    /// <summary>Cyclic one-shot turns in roster order; HOLED, CAPPED and WITHDRAWN players are skipped.</summary>
    public static class TurnScheduler
    {
        public const int None = -1;

        public static int FirstActive(IReadOnlyList<HoleStatus> statusesByRoster)
        {
            for (int i = 0; i < statusesByRoster.Count; i++)
            {
                if (statusesByRoster[i] == HoleStatus.ACTIVE) return i;
            }
            return None;
        }

        /// <summary>Scans forward from the player after <paramref name="currentRosterIndex"/>, wrapping, and may return the same player.</summary>
        public static int NextActive(IReadOnlyList<HoleStatus> statusesByRoster, int currentRosterIndex)
        {
            int count = statusesByRoster.Count;
            if (count == 0) return None;
            for (int step = 1; step <= count; step++)
            {
                int i = ((currentRosterIndex + step) % count + count) % count;
                if (statusesByRoster[i] == HoleStatus.ACTIVE) return i;
            }
            return None;
        }
    }
}
