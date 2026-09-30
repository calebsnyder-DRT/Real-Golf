using Puttshack.Domain;

namespace Puttshack.Rules
{
    /// <summary>Par + N pacing cap (proposed, disclosed as MAX). Evaluated after a stroke and its penalties resolve.</summary>
    public static class PaceCap
    {
        public static int CapValue(int par, SessionSettings settings) => par + settings.PaceCapOverPar;

        public static bool Reached(HoleProgress progress, int par, SessionSettings settings)
        {
            if (!settings.PaceCapEnabled) return false;
            if (progress.Status != HoleStatus.ACTIVE) return false;
            return progress.StrokesSoFar >= CapValue(par, settings);
        }

        public static void ApplyCap(HoleProgress progress, int par, SessionSettings settings)
        {
            progress.Status = HoleStatus.CAPPED;
            progress.CompletionScore = CapValue(par, settings);
            progress.StatusReason = $"Pace cap reached (par + {settings.PaceCapOverPar}); raw count {progress.StrokesSoFar}.";
        }
    }
}
