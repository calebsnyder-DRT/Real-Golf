using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>Hole complete (design 1k), final results (1l), paused (1m) and fault (1n).</summary>
    public static partial class HudUIBuilder
    {
        private static void BuildHoleComplete(Transform root, HudView view)
        {
            view.HoleComplete = FullCard(root, "Hole Complete", out RectTransform c);
            Dim(c, 0.55f);
            const float top = 196, cardsY = top + 198, colW = 360, colH = 383;

            RectTransform title = Row(c, "Title", 24, null, TextAnchor.LowerCenter);
            Anchor(title, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -top), new Vector2(900, 180));
            Fit(title.gameObject);
            view.HoleTitle = Text(title, "Hole", _wide, 110, White, "HOLE 3");
            Twist(title, "Done", 150, "done");

            RectTransform cols = Row(c, "Players", 28, null, TextAnchor.UpperCenter);
            Anchor(cols, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -cardsY), new Vector2(1680, colH));
            Fit(cols.gameObject);
            view.HoleColumnWidth = colW;
            view.HoleRowWidth = 1680;
            view.HoleColumnGap = 28;
            view.HoleScoreSize = 180;
            view.HoleColumns = new HudHoleColumn[HudView.MaxPlayers];
            for (int i = 0; i < view.HoleColumns.Length; i++)
            {
                Image card = Round(cols, $"Player {i + 1}", Midnight.WithAlpha(0.92f), 40);
                card.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var hc = new HudHoleColumn { Root = card.gameObject, Layout = Size(card, colW, colH) };
                RectTransform rt = card.rectTransform;
                hc.Header = Solid(rt, "Header", HudBrand.Player(i));
                HStretch(hc.Header.rectTransform, 0, 77);
                hc.Name = Text(hc.Header.rectTransform, "Name", _bold, 34, Midnight, SampleNames[i], 0f, TextAlignmentOptions.Midline);
                Stretch(hc.Name.rectTransform);
                AutoSize(hc.Name, 20, 34);
                hc.Score = Text(rt, "Score", _wide, 180, White, i == 0 ? "2" : "3", 0f, TextAlignmentOptions.Midline);
                HStretch(hc.Score.rectTransform, 100, 196);

                HudChip word = Chip(rt, "Word", HudBrand.Sun, false, _bold, 28, Midnight, i == 0 ? "BIRDIE" : "PAR", 0f, 28, 54, true);
                Anchor((RectTransform)word.Root.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -293), new Vector2(200, 54));
                Image line = Outline(word.Root.transform, "Line", _linePill, White.WithAlpha(0.5f));
                Stretch(line.rectTransform);
                line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                hc.WordFill = word.Fill;
                hc.WordLine = line;
                hc.Word = word.Text;
                hc.WordFill.enabled = i == 0;
                hc.WordLine.enabled = i != 0;
                if (i != 0) hc.Word.color = White;
                Motion(card, HudMotionKind.Pop, 0.1f + i * 0.15f, 0.45f);
                card.gameObject.SetActive(i < 2);
                view.HoleColumns[i] = hc;
            }

            float footY = cardsY + colH + 48;
            view.HoleComplete.Secondary = Hints(c, view, "Scorecard", PlainHints(), "TAB SCORECARD", new Vector2(0.5f, 1), new Vector2(-16, -(footY + 14)), new Vector2(1, 1));
            view.HoleComplete.Footer = Hints(c, view, "Next", ButtonHints(28, 34, 10), "ENTER NEXT HOLE", new Vector2(0.5f, 1), new Vector2(16, -footY), new Vector2(0, 1));
        }

        private static void BuildRoundComplete(Transform root, HudView view)
        {
            view.RoundComplete = FullCard(root, "Final Results", out RectTransform c);
            Dim(c, 0.7f);
            Rings(c, "Rings", HudBrand.RingsPink, new Vector2(420, 540), 1100, 3f);

            const float x = 120, top = 228;
            view.RoundEyebrow = TextAt(c, "Eyebrow", _condensedBold, 30, White, "FINAL RESULTS  ·  18 HOLES", x, top, 700, 36, 0.2f);
            view.RoundWinner = Disc(c, "Winner", 240, 120, HudBrand.Aqua, "C");
            Place((RectTransform)view.RoundWinner.Root.transform, x, top + 60, 240, 240);
            Motion(view.RoundWinner.Root.transform, HudMotionKind.Pop, 0, 0.6f);
            view.RoundName = TextAt(c, "Name", _wide, 110, White, "CALEB", x, top + 324, 700, 120);
            AutoSize(view.RoundName, 60, 110);
            view.RoundTwist = TextAt(c, "Twist", _twist, 200, White, "wins", x, top + 430, 700, 200, -0.02f);

            const int rows = HudView.MaxPlayers;
            const float w = 960, pad = 40, headY = 40, rowsY = 80, rowH = 92, rowGap = 12;
            view.RoundRowHeight = rowH + rowGap;
            view.RoundBaseHeight = rowsY + 24 + 64 + pad - rowGap;
            Image table = Round(c, "Standings", Midnight.WithAlpha(0.94f), 40);
            RectTransform t = table.rectTransform;
            Anchor(t, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-100, 0), new Vector2(w, view.RoundBaseHeight + 4 * view.RoundRowHeight));
            view.RoundTable = t;

            void Head(string text, float hx, float hw, TextAlignmentOptions align) =>
                TextAt(t, "Head " + text, _condensed, 22, Muted, text, hx, headY, hw, 32, 0.12f, align);
            Head("RANK", 64, 96, TextAlignmentOptions.MidlineLeft);
            Head("PLAYER", 160, 300, TextAlignmentOptions.MidlineLeft);
            Head("TOTAL", 516, 140, TextAlignmentOptions.MidlineRight);
            Head("TO PAR", 656, 140, TextAlignmentOptions.MidlineRight);
            Head("THRU", 796, 100, TextAlignmentOptions.MidlineRight);

            string[] totals = { "52", "55", "58", "61", "63", "66" };
            string[] toPar = { "−2", "+1", "+4", "+7", "+9", "+12" };
            view.RoundRows = new HudStandingRow[rows];
            for (int i = 0; i < rows; i++)
            {
                bool first = i == 0;
                Color fg = first ? Midnight : White;
                var r = new HudStandingRow();
                r.Fill = Round(t, $"Row {i + 1}", first ? HudBrand.Sun : White.WithAlpha(0.06f), 24);
                r.Root = r.Fill.gameObject;
                RectTransform row = r.Fill.rectTransform;
                Place(row, pad, rowsY + i * view.RoundRowHeight, w - pad * 2, rowH);
                r.Rank = TextAt(row, "Rank", _wide, 44, fg, (i + 1).ToString(), 24, 0, 96, rowH);
                r.Disc = Disc(row, "Disc", 52, 24, HudBrand.Player(i), SampleNames[i].Substring(0, 1));
                Place((RectTransform)r.Disc.Root.transform, 120, 20, 52, 52);
                r.Disc.Ring.enabled = true;
                r.Disc.Ring.color = first ? Midnight : HudBrand.Player(i);
                r.Name = TextAt(row, "Name", _bold, 38, fg, SampleNames[i], 188, 0, 290, rowH);
                AutoSize(r.Name, 24, 38);
                r.Total = TextAt(row, "Total", _wide, 44, fg, totals[i], 476, 0, 140, rowH, 0f, TextAlignmentOptions.MidlineRight);
                r.ToPar = TextAt(row, "To Par", _wide, 44, fg, toPar[i], 616, 0, 140, rowH, 0f, TextAlignmentOptions.MidlineRight);
                r.Thru = TextAt(row, "Thru", _bold, 32, fg, "F", 756, 0, 100, rowH, 0f, TextAlignmentOptions.MidlineRight);
                Motion(r.Fill, HudMotionKind.Pop, 0.2f + i * 0.12f, 0.45f);
                r.Root.SetActive(i < 4);
                view.RoundRows[i] = r;
            }

            view.RoundComplete.Secondary = Hints(t, view, "Scorecard", PlainHints(), "TAB SCORECARD", new Vector2(0, 0), new Vector2(pad + 8, pad + 12));
            view.RoundComplete.Footer = Hints(t, view, "Continue", ButtonHints(28, 34, 10), "ENTER PRO SHOP", new Vector2(1, 0), new Vector2(-pad, pad));
        }

        private static void BuildPaused(Transform root, HudView view)
        {
            view.Paused = FullCard(root, "Paused", out RectTransform c);
            Dim(c, 0.78f);
            const float top = 279;
            Image bar1 = Round(c, "Bar L", White, Pill);
            Place(bar1.rectTransform, 890, top, 56, 180);
            Image bar2 = Round(c, "Bar R", White, Pill);
            Place(bar2.rectTransform, 974, top, 56, 180);
            TextAt(c, "Title", _wide, 140, White, "PAUSED", 0, top + 212, 1920, 150, 0f, TextAlignmentOptions.Midline);
            HudChip saved = Chip(c, "Saved", HudBrand.Aqua, false, _bold, 32, Midnight, "ROUND SAVED", 0f, 32, 62, true);
            Anchor((RectTransform)saved.Root.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -(top + 394)), new Vector2(300, 62));
            view.PausedBody = saved.Text;
            view.PausedDetail = TextAt(c, "Detail", _condensed, 28, White.WithAlpha(0.85f), "HOLE 3 OF 18<space=28>·<space=28>CALEB’S SHOT",
                0, top + 488, 1920, 36, 0.1f, TextAlignmentOptions.Midline);

            view.Paused.Footer = Hints(c, view, "Resume", PlainHints(), "ESC RESUME", new Vector2(0.5f, 0), new Vector2(0, 108));
            view.Paused.Secondary = Hints(c, view, "Options", PlainHints(22, 30), "P PRO SHOP   E END ROUND   R RESTART", new Vector2(0.5f, 0), new Vector2(0, 56));
        }

        private static void BuildFault(Transform root, HudView view)
        {
            view.Fault = FullCard(root, "Fault", out RectTransform c);
            Dim(c, 0.82f);
            const float w = 1100, h = 430;
            Image card = Round(c, "Card", HudBrand.Navy, 40);
            RectTransform k = card.rectTransform;
            Anchor(k, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            Image pin = Img(k, "Pin", _logoPin, White, false);
            pin.preserveAspect = true;
            Place(pin.rectTransform, (w - 120) / 2, 64, 120, 20);
            TextAt(k, "Title", _wide, 88, White, "PLEASE WAIT", 0, 108, w, 100, 0f, TextAlignmentOptions.Midline);
            view.FaultBody = TextAt(k, "Body", _book, 40, White, "A staff member is on the way.", 40, 234, w - 80, 52, 0f, TextAlignmentOptions.Midline);
            AutoSize(view.FaultBody, 26, 40);
            HudChip detail = Chip(k, "Detail", Midnight.WithAlpha(0.5f), false, _condensed, 24, White, "CABINET OFFLINE  ·  ROUND SAVED", 0.1f, 26, 50, true);
            Anchor((RectTransform)detail.Root.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -316), new Vector2(500, 50));
            view.FaultDetail = detail.Text;
            view.Fault.Footer = Hints(c, view, "Staff", PlainHints(22, 30), "STAFF ENTER", new Vector2(0.5f, 0), new Vector2(0, 56));
        }
    }
}
