using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>Hole and round summary cards, the pause and fault cards.</summary>
    public static partial class HudUIBuilder
    {
        private static void BuildHoleComplete(Transform root, HudView view)
        {
            const float w = 1000, h = 336;
            view.HoleComplete = Card(root, "Hole Complete", w, h, 120, Cyan);
            var c = (RectTransform)view.HoleComplete.Root.transform;
            view.HoleTitle = FlankedTitle(c, "Title", w, 22, "HOLE 3 COMPLETE", Cyan, 27);
            const float colW = 150f, stripH = 158f;
            RectTransform strip = Child(c, "Standings", 30, 72, w - 60, stripH);
            Image bg = Img(strip, "Fill", _plate, Color.black.WithAlpha(0.22f), true);
            Stretch(bg.rectTransform);
            RectTransform cols = Child(strip, "Columns", 0, 0, w - 60, stripH);
            var layout = cols.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            view.HoleColumns = new HudHoleColumn[HudView.MaxPlayers];
            for (int i = 0; i < view.HoleColumns.Length; i++)
            {
                RectTransform col = Child(cols, $"Player {i + 1}", 0, 0, colW, stripH);
                var hc = new HudHoleColumn { Root = col.gameObject };
                hc.Name = Label(col, "Name", _bold, 23, Palette.Player(i), TextAnchor.MiddleCenter);
                Place(hc.Name.rectTransform, 0, 14, colW, 28);
                AutoSize(hc.Name, 15, 23);
                hc.Name.characterSpacing = 2f;
                hc.Name.text = SampleNames[i];
                hc.Underline = Img(col, "Identity", _soft, Palette.Player(i), true);
                Place(hc.Underline.rectTransform, colW / 2 - 28, 46, 56, 3);
                hc.Score = Label(col, "Score", _display, 58, Cream, TextAnchor.MiddleCenter);
                Place(hc.Score.rectTransform, 0, 52, colW, 66);
                hc.Score.text = "3";
                hc.Word = Label(col, "Word", _bold, 20, Cream.WithAlpha(0.7f), TextAnchor.MiddleCenter);
                Place(hc.Word.rectTransform, 0, 118, colW, 28);
                hc.Word.characterSpacing = 3f;
                hc.Word.text = "PAR";
                view.HoleColumns[i] = hc;
            }
            Footer(c, view, view.HoleComplete, w, "ENTER NEXT HOLE   TAB SCORECARD");
        }

        private static void BuildRoundComplete(Transform root, HudView view)
        {
            const float w = 920, rowH = 54f, top = 176f;
            int rows = HudView.MaxPlayers;
            float h = top + rows * rowH + 82;
            view.RoundComplete = Card(root, "Round Complete", w, h, 64, Gold);
            view.RoundBaseHeight = top + 82;
            view.RoundRowHeight = rowH;
            var c = (RectTransform)view.RoundComplete.Root.transform;
            Image trophy = Img(c, "Trophy", _trophy, Gold, false);
            Place(trophy.rectTransform, 40, 30, 88, 88);
            view.RoundTitle = Label(c, "Title", _display, 66, Cream, TextAnchor.MiddleLeft);
            Place(view.RoundTitle.rectTransform, 148, 22, 700, 74);
            view.RoundTitle.characterSpacing = 2f;
            view.RoundTitle.text = "FINAL RESULTS";
            view.RoundSubtitle = Label(c, "Subtitle", _bold, 27, Cream, TextAnchor.MiddleLeft);
            Place(view.RoundSubtitle.rectTransform, 150, 94, 700, 34);
            view.RoundSubtitle.characterSpacing = 2f;
            view.RoundSubtitle.text = "<color=#00CBE7>CALEB</color> WINS";

            void Head(string text, float x, float width, TextAnchor align)
            {
                TMP_Text t = Label(c, "Head " + text, _semi, 17, Muted, align);
                Place(t.rectTransform, x, 144, width, 24);
                t.characterSpacing = 4f;
                t.text = text;
            }
            Head("RANK", 36, 52, TextAnchor.MiddleCenter);
            Head("PLAYER", 112, 300, TextAnchor.MiddleLeft);
            Head("TOTAL", 530, 110, TextAnchor.MiddleCenter);
            Head("TO PAR", 650, 110, TextAnchor.MiddleCenter);
            Head("THRU", 770, 110, TextAnchor.MiddleCenter);

            view.RoundRows = new HudStandingRow[rows];
            for (int i = 0; i < rows; i++)
            {
                RectTransform row = Child(c, $"Row {i + 1}", 30, top + i * rowH, w - 60, rowH - 6);
                var r = new HudStandingRow { Root = row.gameObject };
                Image band = Img(row, "Band", _plate, Color.white.WithAlpha(i % 2 == 0 ? 0.05f : 0.025f), true);
                Stretch(band.rectTransform);
                r.Badge = Img(row, "Badge", _plate, i == 0 ? Cyan : Palette.Hex(0x2A3434), true);
                Place(r.Badge.rectTransform, 10, 5, 38, 38);
                r.Rank = Label(r.Badge.rectTransform, "Rank", _extra, 22, i == 0 ? Ink : Cream, TextAnchor.MiddleCenter);
                Stretch(r.Rank.rectTransform);
                r.Rank.characterSpacing = 0f;
                r.Rank.text = (i + 1).ToString();
                r.Bar = Img(row, "Identity", _soft, Palette.Player(i), true);
                Place(r.Bar.rectTransform, 66, 9, 5, 30);
                r.Name = Label(row, "Name", _bold, 29, Cream, TextAnchor.MiddleLeft);
                Place(r.Name.rectTransform, 82, 0, 400, rowH - 6);
                r.Name.characterSpacing = 2f;
                r.Name.text = SampleNames[i];
                r.Total = Label(row, "Total", _extra, 31, Cream, TextAnchor.MiddleCenter);
                Place(r.Total.rectTransform, 500, 0, 110, rowH - 6);
                r.Total.text = (3 + i % 3).ToString();
                r.ToPar = Label(row, "To Par", _extra, 31, Cream, TextAnchor.MiddleCenter);
                Place(r.ToPar.rectTransform, 620, 0, 110, rowH - 6);
                r.ToPar.text = "E";
                r.Thru = Label(row, "Thru", _semi, 21, Cream.WithAlpha(0.7f), TextAnchor.MiddleCenter);
                Place(r.Thru.rectTransform, 740, 0, 110, rowH - 6);
                r.Thru.characterSpacing = 2f;
                r.Thru.text = "F";
                view.RoundRows[i] = r;
            }
            Footer(c, view, view.RoundComplete, w, "ENTER PRO SHOP   TAB SCORECARD");
        }

        private static void BuildPaused(Transform root, HudView view)
        {
            const float w = 840, h = 262;
            view.Paused = Card(root, "Paused", w, h, 170, Cyan);
            var c = (RectTransform)view.Paused.Root.transform;
            Image disc = Img(c, "Disc", _disc, Color.white, false);
            Place(disc.rectTransform, 52, 32, 116, 116);
            Image ring = Img(c, "Ring", _ring, Cyan, false);
            Place(ring.rectTransform, 52, 32, 116, 116);
            Image icon = Img(c, "Icon", _pause, Cream, false);
            Place(icon.rectTransform, 78, 58, 64, 64);
            TMP_Text title = Label(c, "Title", _display, 76, Cream, TextAnchor.MiddleLeft);
            Place(title.rectTransform, 196, 26, 600, 84);
            title.characterSpacing = 3f;
            title.text = "PAUSED";
            Image check = Img(c, "Saved Icon", _check, Leaf, false);
            Place(check.rectTransform, 198, 114, 28, 28);
            view.PausedBody = Label(c, "Body", _bold, 24, Cream.WithAlpha(0.85f), TextAnchor.MiddleLeft);
            Place(view.PausedBody.rectTransform, 234, 110, 500, 36);
            view.PausedBody.characterSpacing = 2.5f;
            view.PausedBody.text = "ROUND SAVED";
            Footer(c, view, view.Paused, w, "ENTER RESUME   P PRO SHOP   E END ROUND   R RESTART");
        }

        private static void BuildFault(Transform root, HudView view)
        {
            const float w = 800, h = 280;
            view.Fault = Card(root, "Fault", w, h, 160, Palette.Warning);
            var c = (RectTransform)view.Fault.Root.transform;
            Image icon = Img(c, "Icon", _alert, Palette.Warning, false);
            Place(icon.rectTransform, 50, 44, 96, 96);
            TMP_Text title = Label(c, "Title", _display, 66, Cream, TextAnchor.MiddleLeft);
            Place(title.rectTransform, 170, 30, 600, 74);
            title.characterSpacing = 2f;
            title.text = "PLEASE WAIT";
            view.FaultBody = Label(c, "Body", _semi, 25, Cream.WithAlpha(0.85f), TextAnchor.UpperLeft);
            Place(view.FaultBody.rectTransform, 172, 108, 590, 70);
            view.FaultBody.textWrappingMode = TextWrappingModes.Normal;
            AutoSize(view.FaultBody, 16, 25);
            view.FaultBody.text = "A staff member is on the way.";
            Footer(c, view, view.Fault, w, "STAFF: ENTER");
        }
    }
}
