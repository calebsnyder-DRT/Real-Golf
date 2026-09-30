using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Rules
{
    public sealed class ScorecardCell
    {
        public int HoleIndex;
        public int Par;
        public HoleStatus Status;
        /// <summary>Completed score (holed raw count or capped value); null until the hole resolves.</summary>
        public int? Score;
        /// <summary>Strokes so far on an in-progress hole, displayed with an asterisk. Never added to totals.</summary>
        public int? LiveStrokes;

        public string Display
        {
            get
            {
                if (Score.HasValue) return Status == HoleStatus.CAPPED ? $"{Score}M" : Score.Value.ToString();
                if (LiveStrokes.HasValue) return $"{LiveStrokes}*";
                return Status == HoleStatus.WITHDRAWN ? "WD" : "-";
            }
        }
    }

    public sealed class Subtotal
    {
        public int Strokes;
        public int CompletedHoles;
        public int HoleCount;
        public bool IsPartial => CompletedHoles < HoleCount;
    }

    public sealed class ScorecardRow
    {
        public int PlayerId;
        public string Name;
        public List<ScorecardCell> Cells = new List<ScorecardCell>();
        public int CompletedTotal;
        public int CompletedPar;
        public int Thru;
        public Subtotal Out;
        public Subtotal In;

        public int ToPar => CompletedTotal - CompletedPar;

        public string ToParDisplay
        {
            get
            {
                if (Thru == 0) return "-";
                int v = ToPar;
                return v == 0 ? "E" : v > 0 ? $"+{v}" : v.ToString();
            }
        }
    }

    /// <summary>The scorecard is a derived view over authoritative hole progress. It is never edited directly.</summary>
    public static class ScoreEngine
    {
        public static int? HoleScore(HoleProgress progress)
        {
            switch (progress.Status)
            {
                case HoleStatus.HOLED:
                    return progress.Played + progress.Penalties;
                case HoleStatus.CAPPED:
                    return progress.CompletionScore;
                default:
                    return null;
            }
        }

        /// <param name="pars">Par for each hole in course order.</param>
        /// <param name="progressByHole">This player's progress per hole; missing entries mean NOT_STARTED.</param>
        public static ScorecardRow BuildRow(int playerId, string name, IReadOnlyList<int> pars, IReadOnlyList<HoleProgress> progressByHole)
        {
            var row = new ScorecardRow { PlayerId = playerId, Name = name };
            var outTotal = new Subtotal { HoleCount = System.Math.Min(9, pars.Count) };
            var inTotal = new Subtotal { HoleCount = System.Math.Max(0, pars.Count - 9) };

            for (int h = 0; h < pars.Count; h++)
            {
                HoleProgress progress = h < progressByHole.Count ? progressByHole[h] : null;
                var cell = new ScorecardCell
                {
                    HoleIndex = h,
                    Par = pars[h],
                    Status = progress?.Status ?? HoleStatus.NOT_STARTED,
                };

                if (progress != null)
                {
                    cell.Score = HoleScore(progress);
                    if (!cell.Score.HasValue && progress.Status == HoleStatus.ACTIVE && progress.StrokesSoFar > 0)
                        cell.LiveStrokes = progress.StrokesSoFar;
                }

                if (cell.Score.HasValue)
                {
                    row.CompletedTotal += cell.Score.Value;
                    row.CompletedPar += cell.Par;
                    row.Thru++;
                    Subtotal nine = h < 9 ? outTotal : inTotal;
                    nine.Strokes += cell.Score.Value;
                    nine.CompletedHoles++;
                }

                row.Cells.Add(cell);
            }

            row.Out = outTotal;
            row.In = inTotal;
            return row;
        }

        /// <summary>Label for the next attempted stroke, including penalties already incurred.</summary>
        public static int NextStrokeNumber(HoleProgress progress) => progress.StrokesSoFar + 1;
    }
}
