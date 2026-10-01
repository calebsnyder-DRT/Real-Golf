using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>The paper scorecard overlay. Column widths are laid out at runtime by <see cref="HudView"/> to fit the hole count.</summary>
    public static partial class HudUIBuilder
    {
        private static void BuildScorecard(Transform root, HudView view)
        {
            RectTransform sc = Layer(root, "Scorecard");
            view.Scorecard = sc.gameObject;
            Image dim = Img(sc, "Dim", null, Color.black.WithAlpha(0.35f), false);
            Stretch(dim.rectTransform);

            const float paperW = 1720f, rowH = 56f, headH = 96f, gridTop = 196f;
            int rows = HudView.MaxPlayers;
            float baseH = gridTop + headH + 108f;
            RectTransform paper = Corner(sc, "Paper", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(paperW, baseH + rows * rowH));
            view.ScorePaper = paper;
            view.ScorePaperBaseHeight = baseH;
            view.ScoreRowHeight = rowH;
            view.ScoreHeadHeight = headH;
            view.ScorePaperMargin = 56f;
            Image shadow = Img(paper, "Shadow", _plate, Color.black.WithAlpha(0.3f), true);
            Inset(shadow.rectTransform, -2);
            shadow.rectTransform.anchoredPosition = new Vector2(6, -8);
            Image sheet = Img(paper, "Sheet", _paper, Color.white, true);
            Stretch(sheet.rectTransform);
            Image frame = Img(paper, "Frame", _paperLine, Ink.WithAlpha(0.4f), true);
            Stretch(frame.rectTransform);

            Image word = Img(paper, "Wordmark", _wordInk, Color.white, false);
            word.preserveAspect = true;
            Place(word.rectTransform, 56, 30, 180, 66);
            TMP_Text title = Label(paper, "Title", _display, 80, Ink, TextAnchor.MiddleLeft);
            Place(title.rectTransform, 54, 94, 800, 88);
            title.characterSpacing = 2f;
            title.text = "SCORECARD";
            view.ScoreHoleOf = Label(paper, "Hole Of", _bold, 34, Ink, TextAnchor.MiddleRight);
            Anchor(view.ScoreHoleOf.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -114), new Vector2(320, 48));
            view.ScoreHoleOf.characterSpacing = 2f;
            view.ScoreHoleOf.text = "HOLE <color=#008CA1>3</color> OF 18";
            view.ScoreHints = HintRow(paper, "Close Hint", 36f, _bold, 22f, Ink, false);
            Anchor(view.ScoreHints.Root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-56, -120), view.ScoreHints.Root.sizeDelta);
            view.SetHints(view.ScoreHints, "TAB CLOSE");

            RectTransform grid = Corner(paper, "Grid", new Vector2(0, 1), new Vector2(56, -gridTop), new Vector2(paperW - 112, headH + rows * rowH));
            view.ScoreGrid = grid;

            view.ScoreColumnBand = Img(grid, "Current Hole", _soft, Palette.Cyan.WithAlpha(0.14f), true);
            Place(view.ScoreColumnBand.rectTransform, 300, -4, 70, headH + rows * rowH + 6);

            Image headRule = Img(grid, "Head Rule", null, Ink.WithAlpha(0.3f), false);
            HStretch(headRule.rectTransform, headH - 4, 2f);

            int cols = HudView.ScoreColumns;
            view.ScoreHeader = new TMP_Text[cols];
            view.ScorePar = new TMP_Text[cols];
            for (int col = 0; col < cols; col++)
            {
                TextAnchor align = col == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                view.ScoreHeader[col] = Label(grid, $"Head {col}", _semi, 20, Ink.WithAlpha(0.7f), align);
                Place(view.ScoreHeader[col].rectTransform, col * 60, 0, 60, 44);
                view.ScoreHeader[col].characterSpacing = 2f;
                view.ScorePar[col] = Label(grid, $"Par {col}", _bold, 23, Palette.MutedGreenGray, align);
                Place(view.ScorePar[col].rectTransform, col * 60, 44, 60, 44);
                if (col != 0) continue;
                view.ScoreHeader[col].margin = new Vector4(22, 0, 0, 0);
                view.ScorePar[col].margin = new Vector4(22, 0, 0, 0);
            }

            view.ScoreRows = new HudScoreRow[rows];
            for (int r = 0; r < rows; r++)
            {
                RectTransform rowRt = Layer(grid, $"Row {r + 1}");
                HStretch(rowRt, headH + r * rowH, rowH);
                var row = new HudScoreRow { Root = rowRt.gameObject };
                row.Band = Img(rowRt, "Band", _soft, Ink.WithAlpha(r % 2 == 0 ? 0.05f : 0f), true);
                Stretch(row.Band.rectTransform);
                row.Bar = Img(rowRt, "Identity", _soft, Palette.Player(r), true);
                Place(row.Bar.rectTransform, 4, 11, 6, rowH - 22);
                row.Marks = new Image[HudView.MaxHoles];
                for (int hIndex = 0; hIndex < HudView.MaxHoles; hIndex++)
                {
                    Image mark = Img(rowRt, $"Mark {hIndex + 1}", _scoreMarks[0], Ink.WithAlpha(0.85f), false);
                    Anchor(mark.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -rowH / 2), new Vector2(42, 42));
                    mark.enabled = false;
                    row.Marks[hIndex] = mark;
                }
                row.Cells = new TMP_Text[cols];
                for (int col = 0; col < cols; col++)
                {
                    bool name = col == 0;
                    TMP_Text cell = Label(rowRt, $"Cell {col}", name ? _bold : _semi, name ? 25 : 27, Ink, name ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                    Place(cell.rectTransform, col * 60, 0, 60, rowH);
                    cell.characterSpacing = name ? 2f : 0.5f;
                    if (name) cell.margin = new Vector4(22, 0, 0, 0);
                    row.Cells[col] = cell;
                }
                view.ScoreRows[r] = row;
            }

            BuildLegend(paper);
        }

        private static void BuildLegend(RectTransform paper)
        {
            RectTransform legend = Corner(paper, "Legend", new Vector2(0, 0), new Vector2(56, 28), new Vector2(1200, 34));
            var lay = legend.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.spacing = 8;
            lay.childAlignment = TextAnchor.MiddleLeft;
            lay.childControlWidth = true;
            lay.childControlHeight = false;
            lay.childForceExpandWidth = lay.childForceExpandHeight = false;

            void Mark(Sprite s)
            {
                Image img = Img(legend, "Mark", s, Ink.WithAlpha(0.8f), false);
                img.rectTransform.sizeDelta = new Vector2(30, 30);
                var le = img.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = 30;
            }
            void Entry(string s, Color color, float gapAfter)
            {
                TMP_Text t = Label(legend, s, _semi, 19, color, TextAnchor.MiddleLeft);
                t.rectTransform.sizeDelta = new Vector2(100, 30);
                t.characterSpacing = 2f;
                t.text = s;
                if (gapAfter <= 0) return;
                var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(legend, false);
                gap.GetComponent<LayoutElement>().preferredWidth = gapAfter;
                ((RectTransform)gap.transform).sizeDelta = new Vector2(gapAfter, 30);
            }

            Color ink = Ink.WithAlpha(0.75f);
            Mark(_scoreMarks[1]); Entry("EAGLE", ink, 22);
            Mark(_scoreMarks[0]); Entry("BIRDIE", ink, 22);
            Mark(_scoreMarks[2]); Entry("BOGEY", ink, 22);
            Mark(_scoreMarks[3]); Entry("DOUBLE+", ink, 30);
            Entry("2*", Palette.CyanOnLight, 0); Entry("IN PROGRESS", ink, 30);
            Entry("M", Ink, 0); Entry("MAX (PACE CAP)", ink, 0);
        }
    }
}
