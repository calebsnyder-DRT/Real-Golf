using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Session;
using TMPro;
using UnityEngine;

namespace Puttshack.Runtime.Hud
{
    /// <summary>Hole complete, final results, pause and fault cards.</summary>
    public sealed partial class HudView
    {
        [Header("Hole complete")]
        public HudCard HoleComplete;
        public TMP_Text HoleTitle;
        public HudHoleColumn[] HoleColumns = new HudHoleColumn[0];
        /// <summary>Column width at the design's two-player layout; more players shrink the columns to fit the row.</summary>
        public float HoleColumnWidth = 360f;
        public float HoleRowWidth = 1680f;
        public float HoleColumnGap = 28f;
        public float HoleScoreSize = 180f;

        [Header("Final results")]
        public HudCard RoundComplete;
        public TMP_Text RoundEyebrow, RoundName, RoundTwist;
        public HudDisc RoundWinner;
        public HudStandingRow[] RoundRows = new HudStandingRow[0];
        public RectTransform RoundTable;
        /// <summary>Standings panel height without rows; each shown row adds <see cref="RoundRowHeight"/>.</summary>
        public float RoundBaseHeight = 200f;
        public float RoundRowHeight = 104f;

        [Header("Paused & fault")]
        public HudCard Paused;
        public TMP_Text PausedBody, PausedDetail;
        public HudCard Fault;
        public TMP_Text FaultBody, FaultDetail;

        // ---------------------------------------------------------------- Hole complete

        private void RenderHoleComplete(SessionSnapshot snap)
        {
            HoleDefinition hole = snap.Hole;
            HoleTitle.text = $"HOLE {hole.Number}";
            int count = Mathf.Min(snap.Data.Players.Count, HoleColumns.Length);
            float width = Mathf.Min(HoleColumnWidth, (HoleRowWidth - HoleColumnGap * Mathf.Max(0, count - 1)) / Mathf.Max(1, count));
            for (int i = 0; i < HoleColumns.Length; i++)
            {
                HudHoleColumn col = HoleColumns[i];
                bool present = i < count;
                SetActive(col.Root, present);
                if (!present) continue;
                PlayerState p = snap.Data.Players[i];
                HoleProgress hp = snap.ProgressFor(i);
                Color identity = Identity(p);
                col.Layout.preferredWidth = width;
                col.Header.color = identity;
                col.Name.text = p.Name.ToUpperInvariant();
                col.Name.color = HudBrand.TextOn(identity);
                col.Score.fontSize = HoleScoreSize * width / HoleColumnWidth;
                col.Score.text = hp == null ? "-" : hp.Status == HoleStatus.WITHDRAWN ? "WD" : (hp.CompletionScore ?? hp.StrokesSoFar).ToString();

                string word = string.Empty;
                bool under = false;
                if (hp != null && hp.Status == HoleStatus.HOLED)
                {
                    int diff = hp.CompletionScore.GetValueOrDefault() - hole.Par;
                    word = ToParWord(diff).ToUpperInvariant();
                    under = diff < 0;
                }
                else if (hp != null && hp.Status == HoleStatus.CAPPED) word = "MAX";
                else if (hp != null && hp.Status == HoleStatus.WITHDRAWN) word = "WITHDRAWN";
                col.Word.text = word;
                col.Word.color = under ? Midnight : White;
                col.WordFill.enabled = under && word.Length > 0;
                col.WordLine.enabled = !under && word.Length > 0;
            }
            // ENTER opens the running scorecard; the next hole (or the final results) is one more ENTER from there.
            SetHints(HoleComplete.Footer, "ENTER SCORECARD");
            SetHints(HoleComplete.Secondary, string.Empty);
        }

        // ---------------------------------------------------------------- Final results

        private void RenderRoundComplete(HudContext ctx, SessionSnapshot snap)
        {
            bool incomplete = snap.Data.Completion == RoundCompletion.Incomplete;
            var rows = new List<ScorecardRow>(snap.Scorecard);
            rows.Sort((a, b) =>
            {
                int byThru = b.Thru.CompareTo(a.Thru);
                return byThru != 0 ? byThru : a.CompletedTotal.CompareTo(b.CompletedTotal);
            });

            int rank = 0, leaders = 0;
            for (int i = 0; i < RoundRows.Length; i++)
            {
                HudStandingRow row = RoundRows[i];
                bool present = i < rows.Count;
                SetActive(row.Root, present);
                if (!present) continue;
                ScorecardRow r = rows[i];
                bool tied = i > 0 && r.Thru == rows[i - 1].Thru && r.CompletedTotal == rows[i - 1].CompletedTotal;
                if (!tied) rank = i + 1;
                if (rank == 1) leaders++;
                Color identity = RowIdentity(snap, r, i);
                bool first = rank == 1;
                Color fg = first ? Midnight : White;
                row.Fill.color = first ? HudBrand.Sun : White.WithAlpha(0.06f);
                SetDisc(row.Disc, r.Name, identity, first ? Midnight : identity);
                row.Rank.text = (tied ? "T" : "") + rank;
                row.Name.text = r.Name.ToUpperInvariant();
                row.Total.text = r.Thru > 0 ? r.CompletedTotal.ToString() : "-";
                row.ToPar.text = r.ToParDisplay.Replace("-", "−");
                row.Thru.text = r.Thru < snap.Data.HoleCount ? r.Thru.ToString() : "F";
                row.Rank.color = row.Name.color = row.Total.color = row.ToPar.color = row.Thru.color = fg;
            }

            int shown = Mathf.Min(rows.Count, RoundRows.Length);
            RoundTable.sizeDelta = new Vector2(RoundTable.sizeDelta.x, RoundBaseHeight + shown * RoundRowHeight);
            RoundEyebrow.text = incomplete ? "ROUND ENDED  ·  SCORES SAVED" : $"FINAL RESULTS  ·  {snap.Data.HoleCount} HOLES";

            bool any = rows.Count > 0;
            SetActive(RoundWinner.Root, any);
            if (any)
            {
                ScorecardRow winner = rows[0];
                bool shared = leaders > 1;
                bool unplayed = winner.Thru == 0;
                SetDisc(RoundWinner, winner.Name, RowIdentity(snap, winner, 0), Color.clear);
                SetActive(RoundWinner.Root, !shared && !unplayed);
                if (unplayed)
                {
                    RoundName.text = "ROUND";
                    RoundTwist.text = "ended";
                }
                else
                {
                    RoundName.text = shared ? "SHARED" : winner.Name.ToUpperInvariant();
                    RoundTwist.text = shared ? "win" : incomplete ? "leads" : "wins";
                }
            }
            else
            {
                RoundName.text = string.Empty;
                RoundTwist.text = string.Empty;
            }
            if (ctx.PlayAgain)
            {
                SetHints(RoundComplete.Footer, "ENTER PLAY AGAIN");
                SetHints(RoundComplete.Secondary, ctx.HomeInProShop ? "ESC PLAYER SETUP   TAB SCORECARD" : "ESC MENU   TAB SCORECARD");
                return;
            }
            SetHints(RoundComplete.Footer, ctx.HomeInProShop ? "ENTER PRO SHOP" : "ENTER CONTINUE");
            SetHints(RoundComplete.Secondary, "TAB SCORECARD");
        }

        private static Color RowIdentity(SessionSnapshot snap, ScorecardRow r, int fallback)
        {
            int rosterIndex = snap.Data.Players.FindIndex(p => p.Id == r.PlayerId);
            return HudBrand.Player(rosterIndex >= 0 ? snap.Data.Players[rosterIndex].ColorIndex : fallback);
        }

        // ---------------------------------------------------------------- Paused & fault

        private void RenderPaused(SessionSnapshot snap)
        {
            PausedBody.text = "ROUND SAVED";
            PlayerState p = snap.CurrentPlayer;
            string hole = $"HOLE {snap.Data.HoleIndex + 1} OF {snap.Data.HoleCount}";
            PausedDetail.text = p != null ? $"{hole}<space=28>·<space=28>{p.Name.ToUpperInvariant()}’S SHOT" : hole;
            SetHints(Paused.Footer, "ESC RESUME");
            SetHints(Paused.Secondary, "P PRO SHOP   E END ROUND   R RESTART");
        }

        private void RenderFault(SessionSnapshot snap)
        {
            FaultBody.text = "A staff member is on the way.";
            string reason = snap.Data.FaultReason;
            FaultDetail.text = string.IsNullOrEmpty(reason) ? "ROUND SAVED" : $"{reason.ToUpperInvariant()}  ·  ROUND SAVED";
            SetHints(Fault.Footer, "STAFF ENTER");
        }
    }
}
