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
            Dim(sc, 0.55f);

            const float paperW = 1720f, rowH = 56f, headH = 96f, gridTop = 196f;
            int rows = HudView.MaxPlayers;
            float baseH = gridTop + headH + 108f;
            RectTransform paper = Corner(sc, "Paper", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(paperW, baseH + rows * rowH));
            view.ScorePaper = paper;
            view.ScorePaperBaseHeight = baseH;
            view.ScoreRowHeight = rowH;
            view.ScoreHeadHeight = headH;
            view.ScorePaperMargin = 56f;
            Image sheet = Round(paper, "Sheet", White, 40);
            Stretch(sheet.rectTransform);

            Image word = Img(paper, "Wordmark", _wordmark, Midnight, false);
            word.preserveAspect = true;
            Place(word.rectTransform, 56, 40, 200, 34);
            TextAt(paper, "Pro", _condensedBold, 18, Midnight, WidePro, 266, 44, 120, 30, 0.25f);
            TextAt(paper, "Title", _bold, 80, Midnight, "SCORECARD", 54, 94, 800, 88);
            view.ScoreHoleOf = Label(paper, "Hole Of", _bold, 34, Midnight, TextAnchor.MiddleRight);
            Anchor(view.ScoreHoleOf.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -114), new Vector2(320, 48));
            view.ScoreHoleOf.text = "HOLE 3 OF 18";
            HintStyle close = PlainHints(22, 36);
            close.KeyColor = Midnight.WithAlpha(0.5f);
            close.LegendColor = close.LabelColor = Midnight;
            view.ScoreHints = Hints(paper, view, "Close Hint", close, "TAB CLOSE", new Vector2(1, 1), new Vector2(-56, -120));

            RectTransform grid = Corner(paper, "Grid", new Vector2(0, 1), new Vector2(56, -gridTop), new Vector2(paperW - 112, headH + rows * rowH));
            view.ScoreGrid = grid;

            view.ScoreColumnBand = Round(grid, "Current Hole", HudBrand.Pink.WithAlpha(0.16f), 16);
            Place(view.ScoreColumnBand.rectTransform, 300, -4, 70, headH + rows * rowH + 6);

            Image headRule = Solid(grid, "Head Rule", Midnight.WithAlpha(0.3f));
            HStretch(headRule.rectTransform, headH - 4, 2f);

            int cols = HudView.ScoreColumns;
            view.ScoreHeader = new TMP_Text[cols];
            view.ScorePar = new TMP_Text[cols];
            for (int col = 0; col < cols; col++)
            {
                TextAnchor align = col == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                view.ScoreHeader[col] = Label(grid, $"Head {col}", _condensed, 20, Midnight.WithAlpha(0.7f), align);
                Place(view.ScoreHeader[col].rectTransform, col * 60, 0, 60, 44);
                view.ScoreHeader[col].characterSpacing = 8f;
                view.ScorePar[col] = Label(grid, $"Par {col}", _condensedBold, 23, Midnight.WithAlpha(0.6f), align);
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
                row.Band = Round(rowRt, "Band", Midnight.WithAlpha(r % 2 == 0 ? 0.05f : 0f), 12);
                Stretch(row.Band.rectTransform);
                row.Bar = Round(rowRt, "Identity", HudBrand.Player(r), Pill);
                Place(row.Bar.rectTransform, 4, 11, 6, rowH - 22);
                row.Marks = new Image[HudView.MaxHoles];
                for (int hIndex = 0; hIndex < HudView.MaxHoles; hIndex++)
                {
                    Image mark = Img(rowRt, $"Mark {hIndex + 1}", _scoreMarks[0], Midnight.WithAlpha(0.85f), false);
                    Anchor(mark.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -rowH / 2), new Vector2(42, 42));
                    mark.enabled = false;
                    row.Marks[hIndex] = mark;
                }
                row.Cells = new TMP_Text[cols];
                for (int col = 0; col < cols; col++)
                {
                    bool name = col == 0;
                    TMP_Text cell = Label(rowRt, $"Cell {col}", name ? _bold : _condensedBold, name ? 25 : 27, Midnight, name ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
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
                Image img = Img(legend, "Mark", s, Midnight.WithAlpha(0.8f), false);
                img.rectTransform.sizeDelta = new Vector2(30, 30);
                var le = img.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = 30;
            }
            void Entry(string s, Color color, float gapAfter)
            {
                TMP_Text t = Text(legend, s, _condensed, 19, color, s, 0.08f);
                t.rectTransform.sizeDelta = new Vector2(100, 30);
                if (gapAfter <= 0) return;
                var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(legend, false);
                gap.GetComponent<LayoutElement>().preferredWidth = gapAfter;
                ((RectTransform)gap.transform).sizeDelta = new Vector2(gapAfter, 30);
            }

            Color ink = Midnight.WithAlpha(0.75f);
            Mark(_scoreMarks[1]); Entry("EAGLE", ink, 22);
            Mark(_scoreMarks[0]); Entry("BIRDIE", ink, 22);
            Mark(_scoreMarks[2]); Entry("BOGEY", ink, 22);
            Mark(_scoreMarks[3]); Entry("DOUBLE+", ink, 30);
            Entry("2*", HudBrand.Plum, 0); Entry("IN PROGRESS", ink, 30);
            Entry("M", Midnight, 0); Entry("MAX (PACE CAP)", ink, 0);
        }
    }
}
