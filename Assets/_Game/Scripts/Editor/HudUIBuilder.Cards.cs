using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>Pre-round cards: attract (design 1g), round setup (1h) and the hole briefing (1i).</summary>
    public static partial class HudUIBuilder
    {
        /// <summary>The design's 0.6 em "PRO" lock-up under the wordmark; TMP drops very wide tracking, so spaces carry it.</summary>
        private const string WidePro = "P R O";

        private static void BuildAttract(Transform root, HudView view)
        {
            view.Attract = FullCard(root, "Attract", out RectTransform c);
            Image fade = Img(c, "Fade", _fadeH, Midnight.WithAlpha(0.92f), false);
            Stretch(fade.rectTransform);

            const float x = 120f;
            Image word = Img(c, "Wordmark", _wordmark, White, false);
            word.preserveAspect = true;
            Place(word.rectTransform, x, 246, 520, 87);
            TextAt(c, "Pro", _condensedBold, 40, White, WidePro, x, 347, 300, 48, 0.25f);
            TMP_Text headline = TextAt(c, "Headline", _bold, 96, White, "THE 18-HOLE GOLF EXPERIENCE", x, 435, 900, 192, 0f, TextAlignmentOptions.TopLeft);
            headline.textWrappingMode = TextWrappingModes.Normal;
            headline.lineSpacing = -20f;

            RectTransform chips = Row(c, "Chips", 14, null, TextAnchor.MiddleLeft);
            Place(chips, x, 667, 600, 54);
            Chip(chips, "Players", White.WithAlpha(0.5f), true, _condensedBold, 28, White, "1–6 PLAYERS", 0.1f, 26, 54, false);
            Chip(chips, "Holes", White.WithAlpha(0.5f), true, _condensedBold, 28, White, "18 HOLES", 0.1f, 26, 54, false);

            view.Attract.Footer = Hints(c, view, "Start", ButtonHints(36, 40, 14), "ENTER START", new Vector2(0, 1), new Vector2(x, -765));
            Motion(view.Attract.Footer.Root, HudMotionKind.Pulse, 0, 1.8f);

            Image pin = Img(c, "Pin", _logoPin, White, false);
            pin.preserveAspect = true;
            Anchor(pin.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-56, 56), new Vector2(120, 24));
        }

        private static void BuildLobby(Transform root, HudView view)
        {
            view.Lobby = FullCard(root, "Lobby", out RectTransform layer);
            Dim(layer, 0.55f);
            const float w = 1500, h = 870, left = 64, inner = w - left * 2;
            Image card = Round(layer, "Card", Midnight.WithAlpha(0.94f), 40);
            RectTransform c = card.rectTransform;
            Anchor(c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            Motion(card, HudMotionKind.Pop, 0, 0.45f);

            TextAt(c, "Title", _bold, 80, White, "ROUND SETUP", left, 56, 900, 80);
            TextAt(c, "Subtitle", _book, 30, White.WithAlpha(0.85f), "Choose players and difficulty, then tee off.", left, 146, 900, 38);
            Image word = Img(c, "Wordmark", _wordmark, White, false);
            word.preserveAspect = true;
            Place(word.rectTransform, w - left - 260, 96, 260, 44);
            TextAt(c, "Pro", _condensedBold, 22, White, WidePro, w - left - 260, 146, 260, 28, 0.25f, TextAlignmentOptions.MidlineRight);

            // Players
            const float playersY = 228, slotsY = 280, slotH = 226, slotGap = 20;
            view.LobbyCount = TextAt(c, "Count", _condensed, 24, Muted, "PLAYERS  ·  2 OF 6", left, playersY, 600, 30, 0.12f);
            Vector2 topLeft = new Vector2(0, 1), topRight = new Vector2(1, 1);
            Hints(c, view, "Roster Keys", PlainHints(20, 28), "↑↓ ADD / REMOVE", topLeft, new Vector2(w - left, -playersY), topRight);

            int n = HudView.MaxPlayers;
            float slotW = (inner - slotGap * (n - 1)) / n;
            view.LobbySlots = new HudLobbySlot[n];
            for (int i = 0; i < n; i++)
            {
                RectTransform rt = Child(c, $"Slot {i + 1}", left + i * (slotW + slotGap), slotsY, slotW, slotH);
                var slot = new HudLobbySlot { Root = rt.gameObject };
                slot.Fill = Round(rt, "Fill", White.WithAlpha(0.06f), 28);
                Stretch(slot.Fill.rectTransform);
                slot.Outline = Outline(rt, "Open", _lineR28, White.WithAlpha(0.22f));
                Stretch(slot.Outline.rectTransform);
                bool present = i < 2;
                slot.Fill.enabled = present;
                slot.Outline.enabled = !present;
                slot.Disc = Disc(rt, "Disc", 120, 56, present ? HudBrand.Player(i) : Color.clear, present ? SampleNames[i].Substring(0, 1) : "+");
                Place((RectTransform)slot.Disc.Root.transform, (slotW - 120) / 2, 28, 120, 120);
                slot.Disc.Ring.enabled = !present;
                if (!present) slot.Disc.Initial.color = White.WithAlpha(0.5f);
                slot.Name = TextAt(rt, "Name", _bold, 30, White, present ? SampleNames[i] : "OPEN", 0, 162, slotW, 36, 0f, TextAlignmentOptions.Midline);
                AutoSize(slot.Name, 20, 30);
                view.LobbySlots[i] = slot;
            }

            // Difficulty and pace cap
            const float rowY = slotsY + slotH + 44, trackY = rowY + 48, gap = 48;
            float diffW = (inner - gap) * 2f / 3f;
            TextAt(c, "Difficulty Label", _condensed, 24, Muted, "DIFFICULTY", left, rowY, 400, 30, 0.12f);
            Hints(c, view, "Difficulty Keys", PlainHints(20, 28), "←→", topLeft, new Vector2(left + diffW, -rowY), topRight);
            Image seg = Round(c, "Difficulty Track", White.WithAlpha(0.08f), Pill);
            Place(seg.rectTransform, left, trackY, diffW, 92);
            string[] labels = { "AMATEUR", "CLUB", "PRO" };
            float segW = (diffW - 16 - 16) / 3f;
            view.LobbyAssistFill = new Image[3];
            view.LobbyAssistLabel = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                RectTransform s = Child(seg.rectTransform, labels[i], 8 + i * (segW + 8), 8, segW, 76);
                view.LobbyAssistFill[i] = Round(s, "Fill", HudBrand.Pink, Pill);
                Stretch(view.LobbyAssistFill[i].rectTransform);
                view.LobbyAssistFill[i].enabled = i == 1;
                view.LobbyAssistLabel[i] = Text(s, "Label", _bold, 30, i == 1 ? Midnight : White, labels[i], 0f, TextAlignmentOptions.Midline);
                Stretch(view.LobbyAssistLabel[i].rectTransform);
            }

            float paceX = left + diffW + gap;
            TextAt(c, "Pace Label", _condensed, 24, Muted, "PACE CAP", paceX, rowY, 300, 30, 0.12f);
            Hints(c, view, "Pace Keys", PlainHints(20, 28), "C", topLeft, new Vector2(w - left, -rowY), topRight);
            view.LobbyCapTrack = Round(c, "Pace Toggle", HudBrand.Aqua, Pill);
            Place(view.LobbyCapTrack.rectTransform, paceX, trackY + 20, 96, 52);
            Image knob = Round(view.LobbyCapTrack.rectTransform, "Knob", Midnight, Pill);
            Anchor(knob.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(50, 0), new Vector2(40, 40));
            view.LobbyCapKnob = knob.rectTransform;
            view.LobbyCapKnobOff = 6f;
            view.LobbyCapKnobOn = 50f;
            view.LobbyCapText = TextAt(c, "Pace Value", _bold, 32, White, "MAX PAR +5", paceX + 116, trackY + 20, 300, 52);

            // Footer
            const float footY = trackY + 92 + 44;
            Image rule = Solid(c, "Footer Rule", LineColor);
            Place(rule.rectTransform, left, footY, inner, 1);
            view.Lobby.Secondary = Hints(c, view, "Shop", PlainHints(28, 36), "P PRO SHOP", topLeft, new Vector2(left, -(footY + 46)));
            view.Lobby.Footer = Hints(c, view, "Start", ButtonHints(32), "ENTER START ROUND", topLeft, new Vector2(w - left, -(footY + 32)), topRight);
        }

        private static void BuildBriefing(Transform root, HudView view)
        {
            view.Briefing = FullCard(root, "Briefing", out RectTransform layer);
            const float w = 1680, h = 400, blockW = 380, windW = 248;
            Image card = Round(layer, "Card", Midnight.WithAlpha(0.92f), 40);
            RectTransform c = card.rectTransform;
            Anchor(c, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(w, h));
            card.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Motion(card, HudMotionKind.Rise, 0, 0.5f);

            Image block = Solid(c, "Hole Block", HudBrand.Pink);
            Place(block.rectTransform, 0, 0, blockW, h);
            TextAt(c, "Hole Label", _condensedBold, 28, Midnight, "HOLE", 56, 80, 280, 34, 0.14f);
            view.BriefHoleNumber = TextAt(c, "Hole Number", _wide, 180, Midnight, "3", 50, 112, 300, 160);
            view.BriefPar = TextAt(c, "Par", _condensedBold, 30, Midnight, "PAR 3  ·  165 YD", 56, 284, 300, 36, 0.1f);
            AutoSize(view.BriefPar, 20, 30);

            const float mx = blockW + 52, mw = w - blockW - windW - 104;
            TextAt(c, "Plan Label", _condensed, 24, Muted, "HOLE PLAN", mx, 44, 400, 30, 0.12f);
            view.BriefHazard = TextAt(c, "Hazard", _book, 34, White, "The Pacific runs the whole left side: anything over the cliff edge is in the penalty area.", mx, 88, mw, 90, 0f, TextAlignmentOptions.TopLeft);
            Wrap(view.BriefHazard, 24);
            view.BriefPlan = TextAt(c, "Plan", _book, 34, White.WithAlpha(0.85f), "Aim at the right half of the green. Short and right leaves an easy chip from the fairway.", mx, 190, mw, 90, 0f, TextAlignmentOptions.TopLeft);
            Wrap(view.BriefPlan, 24);

            view.BriefPlayer = Badge(c, "Player", new BadgeStyle { Height = 56, Disc = 44, DiscFont = 22, NameSize = 26, PadLeft = 6, Fill = HudBrand.Aqua }, "CALEB TEES OFF", null, true);
            Anchor(view.BriefPlayer.Fill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(mx, -304), view.BriefPlayer.Fill.rectTransform.sizeDelta);
            view.Briefing.Footer = Hints(c, view, "Hints", PlainHints(), "ESC PAUSE   ENTER TEE OFF", new Vector2(0, 1), new Vector2(mx + mw, -315), new Vector2(1, 1));

            const float wx = w - windW;
            Image split = Solid(c, "Wind Rule", LineColor);
            Place(split.rectTransform, wx, 0, 1, h);
            Image dial = Img(c, "Wind Dial", _ringThin, White.WithAlpha(0.22f), false);
            Place(dial.rectTransform, wx + (windW - 140) / 2, 76, 140, 140);
            Image arrow = Img(dial.rectTransform, "Arrow", _arrow, HudBrand.Aqua, false);
            Anchor(arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76, 76));
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -135);
            view.BriefWindArrow = arrow.rectTransform;
            TextAt(c, "Wind Label", _condensed, 22, Muted, "WIND", wx, 232, windW, 28, 0.12f, TextAlignmentOptions.Midline);
            view.BriefWind = TextAt(c, "Wind", _wide, 48, White, "4<size=46%> MPH</size>", wx, 270, windW, 56, 0f, TextAlignmentOptions.Midline);
        }
    }
}
