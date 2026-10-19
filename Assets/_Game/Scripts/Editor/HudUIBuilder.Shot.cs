using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>Shot cards: result at rest and penalties (design 1c, 1f), celebrations (1d, 1e) and the handoff (1j).</summary>
    public static partial class HudUIBuilder
    {
        /// <summary>Sun dots that drift up on the top-tier celebration: x, y, size in reference px.</summary>
        private static readonly int[,] Dots =
        {
            { 620, 640, 22 }, { 780, 720, 14 }, { 900, 600, 30 }, { 1060, 700, 18 }, { 1200, 620, 26 }, { 1320, 740, 14 }, { 700, 820, 18 },
            { 1000, 840, 12 }, { 1150, 860, 22 }, { 1280, 820, 16 }, { 560, 760, 12 }, { 1400, 660, 20 }, { 860, 500, 12 }, { 1100, 520, 16 },
        };

        private static void BuildResult(Transform root, HudView view)
        {
            view.Result = FullCard(root, "Shot Result", out RectTransform layer);

            RectTransform roster = Row(layer, "Roster", 14);
            Anchor(roster, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(600, 64));
            Fit(roster.gameObject);
            var pill = new BadgeStyle
            {
                Height = 64, Disc = 48, DiscFont = 24, NameSize = 24, PadRight = 22, Fill = Midnight.WithAlpha(0.86f),
                ExtraFont = _wide, ExtraSize = 24,
            };
            view.ResultRoster = new HudBadge[HudView.MaxPlayers];
            for (int i = 0; i < view.ResultRoster.Length; i++)
            {
                HudBadge b = Badge(roster, $"Player {i + 1}", pill, SampleNames[i], i == 0 ? "1" : "0", false);
                b.Disc.color = HudBrand.Player(i);
                b.Initial.color = Midnight;
                b.Name.color = b.Extra.color = White;
                b.Root.SetActive(i < 2);
                view.ResultRoster[i] = b;
            }

            const float w = 1400, panelH = 336, tabH = 72, overlap = 28;
            RectTransform card = Corner(layer, "Card", new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(w, panelH + tabH - overlap));
            view.ResultCard = card;
            view.ResultWidth = w;
            view.ResultPenaltyWidth = 1120;
            Motion(card, HudMotionKind.Rise, 0, 0.5f);

            view.ResultPanel = Round(card, "Panel", Midnight.WithAlpha(0.92f), 36);
            RectTransform p = view.ResultPanel.rectTransform;
            p.anchorMin = new Vector2(0, 0);
            p.anchorMax = new Vector2(1, 0);
            p.pivot = new Vector2(0.5f, 0);
            p.anchoredPosition = Vector2.zero;
            p.sizeDelta = new Vector2(0, panelH);

            view.ResultTitle = TextAt(p, "Title", _wide, 96, White, "FAIRWAY", 48, 60, 560, 92);
            AutoSize(view.ResultTitle, 48, 96);
            view.ResultSubtitle = TextAt(p, "Subtitle", _condensed, 30, White, "12 YD TO PIN", 48, 156, 560, 36, 0.1f);

            RectTransform stats = Row(p, "Stats", 16, null, TextAnchor.LowerRight);
            Anchor(stats, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -56), new Vector2(734, 142));
            string[] labels = { "CARRY", "ROLL", "TOTAL" };
            string[] values = { "142", "11", "153" };
            float[] widths = { 224, 196, 282 };
            view.ResultStats = new HudStat[3];
            for (int i = 0; i < 3; i++)
            {
                bool hero = i == 2;
                float tileH = hero ? 142 : 134;
                var s = new HudStat();
                s.Fill = Round(stats, labels[i], hero ? HudBrand.Aqua : White.WithAlpha(0.08f), 24);
                s.Root = s.Fill.gameObject;
                Size(s.Fill, widths[i], tileH);
                Color text = hero ? Midnight : White;
                s.Label = TextAt(s.Fill.transform, "Label", hero ? _condensedBold : _condensed, 22, hero ? text : Muted, labels[i], 28, 18, widths[i] - 56, 26, 0.12f);
                float size = hero ? 80 : 64;
                s.Value = TextAt(s.Fill.transform, "Value", _wide, size, text, values[i] + "<size=40%> YD</size>", 28, 50, widths[i] - 52, size + 6);
                AutoSize(s.Value, 32, size);
                if (hero) Motion(s.Fill, HudMotionKind.Pop, 0.6f, 0.5f);
                else Motion(s.Fill, HudMotionKind.Pop, 0.25f + i * 0.15f, 0.45f);
                view.ResultStats[i] = s;
            }

            view.ResultPenalty = Chip(p, "Penalty", White, false, _bold, 30, Midnight, "+1  ·  DROP TAKEN", 0f, 28, 60, true);
            Anchor((RectTransform)view.ResultPenalty.Root.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -88), new Vector2(400, 60));
            view.ResultPenalty.Root.SetActive(false);

            Image rule = Solid(p, "Rule", LineColor);
            Anchor(rule.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -234), new Vector2(0, 1));
            rule.rectTransform.anchorMin = new Vector2(0, 1);
            rule.rectTransform.anchorMax = new Vector2(1, 1);
            rule.rectTransform.sizeDelta = new Vector2(-96, 1);
            view.ResultInput = TextAt(p, "Input", _condensed, 24, White.WithAlpha(0.85f), "LANE 11<space=28>POWER 66%<space=28>5 IRON", 48, 266, 900, 36, 0.1f);
            view.Result.Footer = Hints(p, view, "Continue", PlainHints(), "ENTER CONTINUE", new Vector2(1, 1), new Vector2(-48, -267));

            view.ResultTab = Badge(card, "Tab", new BadgeStyle
            {
                Height = tabH, Disc = 56, DiscFont = 28, NameSize = 30, PadRight = 26, Spacing = 14, Fill = HudBrand.Aqua,
                ExtraFont = _condensedBold, ExtraSize = 22, ExtraTracking = 0.12f,
            }, "CALEB", "SHOT 1", true);
            Anchor(view.ResultTab.Fill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, 0), view.ResultTab.Fill.rectTransform.sizeDelta);
        }

        private static void BuildCelebrate(Transform root, HudView view)
        {
            view.Celebrate = FullCard(root, "Celebration", out RectTransform c);
            Dim(c, 0.72f);
            var centre = new Vector2(960, 540);
            view.CelebratePinkRings = Rings(c, "Pink Rings", HudBrand.RingsPink, centre, 1500, 2.4f);
            view.CelebrateBlueRings = Rings(c, "Blue Rings", HudBrand.RingsBlue, centre, 1700, 2.2f);
            RectTransform dots = Layer(c, "Dots");
            for (int i = 0; i < Dots.GetLength(0); i++)
            {
                Image d = Round(dots, $"Dot {i + 1}", HudBrand.Sun, Pill);
                Place(d.rectTransform, Dots[i, 0], Dots[i, 1], Dots[i, 2], Dots[i, 2]);
                Motion(d, HudMotionKind.Drift, i * 0.17f, 2.6f);
            }
            view.CelebrateDots = dots.gameObject;
            view.CelebrateBlueRings.SetActive(false);
            view.CelebrateDots.SetActive(false);

            const float top = 297, wordY = top + 84, wordH = 340;
            view.CelebratePlayer = Badge(c, "Player", new BadgeStyle
            {
                Height = 76, Disc = 60, DiscFont = 30, NameSize = 34, PadRight = 28, Spacing = 14, Fill = HudBrand.Aqua,
            }, "CALEB", null, true);
            Anchor(view.CelebratePlayer.Fill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -top), view.CelebratePlayer.Fill.rectTransform.sizeDelta);
            Motion(view.CelebratePlayer.Fill, HudMotionKind.Rise, 0, 0.4f);

            view.CelebrateWord = Twist(c, "Word", 340, "birdie");
            Place(view.CelebrateWord.rectTransform, 60, wordY, 1800, wordH);
            AutoSize(view.CelebrateWord, 160, 340);
            Motion(view.CelebrateWord, HudMotionKind.Pop, 0.15f, 0.6f);

            RectTransform ace = Child(c, "Hole In One", 0, wordY, 1920, wordH);
            const float leadW = 640, oneW = 580, gap = 28, start = (1920 - leadW - gap - oneW) / 2;
            TextAt(ace, "Lead", _wide, 140, White, "HOLE IN", start, -70, leadW, wordH, 0f, TextAlignmentOptions.BottomLeft);
            TMP_Text one = Twist(ace, "One", 340, "one");
            Place(one.rectTransform, start + leadW + gap, 0, oneW, wordH);
            one.alignment = TextAlignmentOptions.BottomLeft;
            Motion(ace, HudMotionKind.Pop, 0.15f, 0.6f);
            view.CelebrateAce = ace.gameObject;
            ace.gameObject.SetActive(false);

            view.CelebrateDetail = TextAt(c, "Detail", _bold, 44, White, "HOLE 3<color=#F45197>  •  </color>2 STROKES<color=#F45197>  •  </color>1 UNDER PAR",
                0, wordY + wordH + 8, 1920, 54, 0f, TextAlignmentOptions.Midline);
            Motion(view.CelebrateDetail, HudMotionKind.Rise, 0.5f, 0.45f);

            view.Celebrate.Footer = Hints(c, view, "Continue", PlainHints(), "ENTER CONTINUE", new Vector2(0.5f, 0), new Vector2(0, 56));
        }

        private static void BuildHandoff(Transform root, HudView view)
        {
            view.Handoff = FullCard(root, "Handoff", out RectTransform c);
            Dim(c, 0.6f);
            const float top = 249, disc = 280;
            float discCentre = top + disc / 2;

            view.HandoffHalo = Round(c, "Halo", HudBrand.Pink, Pill);
            Anchor(view.HandoffHalo.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -discCentre), new Vector2(420, 420));
            Motion(view.HandoffHalo, HudMotionKind.Halo, 0, 1.8f);

            view.HandoffDisc = Disc(c, "Disc", disc, 140, HudBrand.Pink, "M");
            Anchor((RectTransform)view.HandoffDisc.Root.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -discCentre), new Vector2(disc, disc));
            Motion(view.HandoffDisc.Root.transform, HudMotionKind.Pop, 0, 0.5f);

            TextAt(c, "Up Next", _condensedBold, 30, White, "UP NEXT", 0, top + disc + 28, 1920, 36, 0.2f, TextAlignmentOptions.Midline);
            view.HandoffName = TextAt(c, "Name", _wide, 130, White, "MAYA", 0, top + disc + 74, 1920, 140, 0f, TextAlignmentOptions.Midline);
            AutoSize(view.HandoffName, 70, 130);

            RectTransform chips = Row(c, "Chips", 14);
            Anchor(chips, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -(top + disc + 242)), new Vector2(900, 60));
            Fit(chips.gameObject);
            string[] samples = { "STROKE 1", "165 YD TO PIN", "TEE" };
            view.HandoffChips = new HudChip[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                view.HandoffChips[i] = Chip(chips, $"Chip {i + 1}", Midnight.WithAlpha(0.86f), false, _bold, 30, White, samples[i], 0f, 28, 60, false);

            view.Handoff.Footer = Hints(c, view, "Hints", PlainHints(), "ESC PAUSE   ENTER READY", new Vector2(0.5f, 0), new Vector2(0, 56));
        }
    }
}
