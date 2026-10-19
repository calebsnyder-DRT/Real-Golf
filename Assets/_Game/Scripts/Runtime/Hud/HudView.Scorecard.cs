using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.Runtime.Hud
{
    /// <summary>The paper scorecard overlay. Column widths are laid out at runtime to fit the hole count.</summary>
    public sealed partial class HudView
    {
        [Header("Scorecard")]
        public GameObject Scorecard;
        public RectTransform ScorePaper;
        public float ScorePaperMargin = 56f;
        public float ScorePaperMinWidth = 1100f;
        /// <summary>Paper height without player rows; each visible row adds <see cref="ScoreRowHeight"/>.</summary>
        public float ScorePaperBaseHeight = 400f;
        public float ScoreRowHeight = 56f;
        /// <summary>Height of the header and par rows above the first player row.</summary>
        public float ScoreHeadHeight = 96f;
        public TMP_Text ScoreHoleOf;
        public RectTransform ScoreGrid;
        public Image ScoreColumnBand;
        public TMP_Text[] ScoreHeader = new TMP_Text[0];
        public TMP_Text[] ScorePar = new TMP_Text[0];
        public HudScoreRow[] ScoreRows = new HudScoreRow[0];
        public HudHintRow ScoreHints;

        private static readonly Color PaperInk = HudBrand.Midnight;
        private static readonly Color UnderPar = HudBrand.Navy;
        private static readonly Color OverPar = HudBrand.Plum;

        private long _scoreRevision = -1;
        private bool _scoreShown;

        private void RenderScorecard(HudContext ctx, SessionSnapshot snap)
        {
            SetActive(Scorecard, ctx.ShowScorecard);
            if (!ctx.ShowScorecard)
            {
                _scoreShown = false;
                return;
            }
            SetHints(ScoreHints, ctx.ScorecardFooter ?? "TAB CLOSE");
            if (_scoreShown && _scoreRevision == snap.Revision) return;
            _scoreShown = true;
            _scoreRevision = snap.Revision;

            List<int> pars = snap.Course.Pars;
            int holes = Mathf.Min(pars.Count, MaxHoles);
            bool split = holes > 9;
            ScoreHoleOf.text = $"HOLE <color={HudBrand.Hex(HudBrand.Plum)}>{Mathf.Min(snap.Data.HoleIndex + 1, Mathf.Max(1, holes))}</color> OF {holes}";

            // Column geometry: player | holes | (OUT, IN) | TOTAL | TO PAR | THRU
            float nameW = 250f, subW = 84f, totalW = 104f, toParW = 104f, thruW = 84f;
            float fixedW = nameW + (split ? subW * 2 : 0) + totalW + toParW + thruW;
            float holeW = Mathf.Clamp((1720f - ScorePaperMargin * 2 - fixedW) / Mathf.Max(1, holes), 52f, 96f);
            float gridW = fixedW + holeW * holes;
            float minGridW = ScorePaperMinWidth - ScorePaperMargin * 2;
            if (gridW < minGridW)
            {
                float k = minGridW / gridW;
                nameW *= k; subW *= k; totalW *= k; toParW *= k; thruW *= k; holeW *= k;
                gridW = minGridW;
            }
            int rowsShown = Mathf.Min(snap.Scorecard.Count, ScoreRows.Length);
            ScorePaper.sizeDelta = new Vector2(Mathf.Max(ScorePaperMinWidth, gridW + ScorePaperMargin * 2), ScorePaperBaseHeight + rowsShown * ScoreRowHeight);
            ScoreGrid.sizeDelta = new Vector2(gridW, ScoreHeadHeight + rowsShown * ScoreRowHeight);

            var x = new float[ScoreColumns];
            var w = new float[ScoreColumns];
            var on = new bool[ScoreColumns];
            float cursor = 0;
            for (int c = 0; c < ScoreColumns; c++)
            {
                float width;
                if (c == 0) width = nameW;
                else if (c <= MaxHoles) width = c <= holes ? holeW : 0;
                else if (c <= MaxHoles + 2) width = split ? subW : 0;
                else if (c == MaxHoles + 3) width = totalW;
                else if (c == MaxHoles + 4) width = toParW;
                else width = thruW;
                x[c] = cursor;
                w[c] = width;
                on[c] = width > 0;
                cursor += width;
            }

            int parTotal = 0;
            for (int h = 0; h < holes; h++) parTotal += pars[h];
            for (int c = 0; c < ScoreColumns; c++)
            {
                string header, par;
                if (c == 0) { header = "PLAYER"; par = "PAR"; }
                else if (c <= MaxHoles) { header = c <= holes ? snap.Course.Holes[c - 1].Number.ToString() : ""; par = c <= holes ? pars[c - 1].ToString() : ""; }
                else if (c == MaxHoles + 1) { header = "OUT"; par = Sum(pars, 0, Mathf.Min(9, holes)).ToString(); }
                else if (c == MaxHoles + 2) { header = "IN"; par = Sum(pars, 9, holes).ToString(); }
                else if (c == MaxHoles + 3) { header = "TOTAL"; par = parTotal.ToString(); }
                else if (c == MaxHoles + 4) { header = "TO PAR"; par = "—"; }
                else { header = "THRU"; par = ""; }
                PlaceCell(ScoreHeader[c], x[c], w[c], on[c], header);
                PlaceCell(ScorePar[c], x[c], w[c], on[c], par);
            }

            int current = snap.Data.HoleIndex;
            bool band = current >= 0 && current < holes && snap.State != SessionState.ROUND_COMPLETE;
            ScoreColumnBand.gameObject.SetActive(band);
            if (band)
            {
                RectTransform b = ScoreColumnBand.rectTransform;
                b.anchoredPosition = new Vector2(x[current + 1], b.anchoredPosition.y);
                b.sizeDelta = new Vector2(holeW, ScoreHeadHeight + rowsShown * ScoreRowHeight + 6f);
            }

            for (int r = 0; r < ScoreRows.Length; r++)
            {
                HudScoreRow row = ScoreRows[r];
                bool present = r < snap.Scorecard.Count;
                SetActive(row.Root, present);
                if (!present) continue;
                ScorecardRow data = snap.Scorecard[r];
                int rosterIndex = snap.Data.Players.FindIndex(p => p.Id == data.PlayerId);
                row.Bar.color = HudBrand.Player(rosterIndex >= 0 ? snap.Data.Players[rosterIndex].ColorIndex : r);
                for (int c = 0; c < ScoreColumns; c++)
                {
                    string text;
                    Color color = PaperInk;
                    if (c == 0) text = data.Name.ToUpperInvariant();
                    else if (c <= MaxHoles)
                    {
                        ScorecardCell cell = c - 1 < data.Cells.Count ? data.Cells[c - 1] : null;
                        text = cell != null && c <= holes ? cell.Display : "";
                        if (cell != null && cell.LiveStrokes.HasValue && !cell.Score.HasValue) color = HudBrand.Plum;
                        Image mark = row.Marks[c - 1];
                        Sprite sprite = cell != null && c <= holes ? MarkFor(cell) : null;
                        mark.sprite = sprite;
                        mark.enabled = sprite != null;
                        if (sprite != null) mark.rectTransform.anchoredPosition = new Vector2(x[c] + w[c] / 2f, mark.rectTransform.anchoredPosition.y);
                    }
                    else if (c == MaxHoles + 1) text = SubtotalText(data.Out);
                    else if (c == MaxHoles + 2) text = SubtotalText(data.In);
                    else if (c == MaxHoles + 3) text = data.Thru > 0 ? data.CompletedTotal.ToString() : "—";
                    else if (c == MaxHoles + 4)
                    {
                        text = data.ToParDisplay;
                        color = data.Thru == 0 || data.ToPar == 0 ? PaperInk : data.ToPar < 0 ? UnderPar : OverPar;
                    }
                    else text = data.Thru.ToString();
                    TMP_Text t = row.Cells[c];
                    PlaceCell(t, x[c], w[c], on[c], text);
                    t.color = color;
                }
            }
        }

        private Sprite MarkFor(ScorecardCell cell)
        {
            if (!cell.Score.HasValue || cell.Status != HoleStatus.HOLED) return null;
            int diff = cell.Score.Value - cell.Par;
            if (diff <= -2) return MarkEagle;
            if (diff == -1) return MarkBirdie;
            if (diff == 1) return MarkBogey;
            if (diff >= 2) return MarkDouble;
            return null;
        }

        private static void PlaceCell(TMP_Text t, float x, float w, bool on, string text)
        {
            t.gameObject.SetActive(on);
            if (!on) return;
            RectTransform rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            t.text = text;
        }

        private static int Sum(List<int> values, int from, int to)
        {
            int s = 0;
            for (int i = from; i < to && i < values.Count; i++) s += values[i];
            return s;
        }

        private static string SubtotalText(Subtotal s)
        {
            if (s == null || s.HoleCount == 0) return "";
            if (s.CompletedHoles == 0) return "—";
            return s.IsPartial ? $"{s.Strokes}p" : s.Strokes.ToString();
        }
    }
}
