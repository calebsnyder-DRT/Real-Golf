using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>Pre-round and shot cards: attract, lobby, briefing, shot result and handoff.</summary>
    public static partial class HudUIBuilder
    {
        private static void BuildAttract(Transform root, HudView view)
        {
            const float w = 760, h = 430;
            view.Attract = Card(root, "Attract", w, h, 150, Cyan);
            var c = (RectTransform)view.Attract.Root.transform;
            Image emblem = Img(c, "Emblem", _emblem, Color.white, false);
            Place(emblem.rectTransform, (w - 192) / 2, 26, 192, 192);
            TMP_Text tag = Label(c, "Tagline", _extra, 34, Cream, TextAnchor.MiddleCenter);
            Place(tag.rectTransform, 0, 228, w, 42);
            tag.characterSpacing = 3f;
            tag.text = "THE 18-HOLE GOLF EXPERIENCE";
            view.AttractBody = Label(c, "Body", _bold, 24, Cyan, TextAnchor.MiddleCenter);
            Place(view.AttractBody.rectTransform, 0, 272, w, 32);
            view.AttractBody.characterSpacing = 3f;
            view.AttractBody.text = "1\u20136 PLAYERS  \u00b7  18 HOLES";
            Footer(c, view, view.Attract, w, "ENTER START");
        }

        private static void BuildLobby(Transform root, HudView view)
        {
            const float w = 1120, h = 440;
            view.Lobby = Card(root, "Lobby", w, h, 92, Cyan);
            var c = (RectTransform)view.Lobby.Root.transform;

            Image emblem = Img(c, "Emblem", _emblemSm, Color.white, false);
            Place(emblem.rectTransform, 30, 24, 108, 108);
            TMP_Text title = Label(c, "Title", _display, 64, Cream, TextAnchor.MiddleLeft);
            Place(title.rectTransform, 156, 22, 640, 70);
            title.characterSpacing = 2f;
            title.text = "ROUND SETUP";
            TMP_Text sub = Label(c, "Subtitle", _semi, 23, Muted, TextAnchor.MiddleLeft);
            Place(sub.rectTransform, 158, 92, 640, 30);
            sub.characterSpacing = 2f;
            sub.text = "CHOOSE PLAYERS AND DIFFICULTY, THEN TEE OFF";
            Image word = Img(c, "Wordmark", _wordCream, Color.white, false);
            word.preserveAspect = true;
            Place(word.rectTransform, w - 32 - 210, 36, 210, 80);
            Image rule = Img(c, "Header Rule", _rule, RuleColor, false);
            Place(rule.rectTransform, 30, 144, w - 60, 6);

            // Players
            SectionHeader(c, "Players Header", 32, 162, 600, _players, "PLAYERS", "up", "down");
            view.LobbyCount = Label(c, "Count", _bold, 21, Cream, TextAnchor.MiddleLeft);
            Place(view.LobbyCount.rectTransform, 190, 162, 200, 30);
            view.LobbyCount.characterSpacing = 2f;
            view.LobbyCount.text = "4 PLAYERS";
            int n = HudView.MaxPlayers;
            view.LobbyPlayers = new HudMedallion[n];
            view.LobbyNames = new TMP_Text[n];
            for (int i = 0; i < n; i++)
            {
                float x = 36 + i * 100;
                view.LobbyPlayers[i] = Medallion(c, x + 12, 206, 70, 36, Palette.Player(i), SampleNames[i].Substring(0, 1));
                view.LobbyNames[i] = Label(c, $"Name {i + 1}", _bold, 19, Palette.Player(i), TextAnchor.MiddleCenter);
                Place(view.LobbyNames[i].rectTransform, x, 282, 94, 26);
                AutoSize(view.LobbyNames[i], 13, 19);
                view.LobbyNames[i].characterSpacing = 1f;
                view.LobbyNames[i].text = SampleNames[i];
            }

            Image split = Img(c, "Divider", _dividerV, RuleColor, false);
            Place(split.rectTransform, 664, 160, 6, 164);

            // Difficulty
            SectionHeader(c, "Difficulty Header", 696, 162, 392, _target, "DIFFICULTY", "left", "right");
            view.LobbyAssistFill = new Image[3];
            view.LobbyAssistLabel = new TMP_Text[3];
            string[] labels = { "AMATEUR", "CLUB", "PRO" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform seg = Child(c, $"Assist {labels[i]}", 696 + i * 132, 202, 124, 46);
                view.LobbyAssistFill[i] = Img(seg, "Fill", _plate, i == 1 ? Cyan : Palette.Hex(0x222A2A), true);
                Stretch(view.LobbyAssistFill[i].rectTransform);
                Image line = Img(seg, "Frame", _plateLine, Cream.WithAlpha(0.22f), true);
                Stretch(line.rectTransform);
                view.LobbyAssistLabel[i] = Label(seg, "Label", _bold, 22, i == 1 ? Ink : Cream.WithAlpha(0.75f), TextAnchor.MiddleCenter);
                Stretch(view.LobbyAssistLabel[i].rectTransform);
                view.LobbyAssistLabel[i].characterSpacing = 2.5f;
                view.LobbyAssistLabel[i].text = labels[i];
            }

            // Pace cap
            SectionHeader(c, "Pace Header", 696, 262, 392, _stopwatch, "PACE CAP", "c");
            view.LobbyCapToggle = Img(c, "Toggle", _toggleOn, Color.white, false);
            Place(view.LobbyCapToggle.rectTransform, 696, 300, 60, 32);
            view.LobbyCapText = Label(c, "Cap", _bold, 23, Cream, TextAnchor.MiddleLeft);
            Place(view.LobbyCapText.rectTransform, 770, 296, 300, 40);
            view.LobbyCapText.characterSpacing = 2f;
            view.LobbyCapText.text = "MAX PAR +5";

            Footer(c, view, view.Lobby, w, "P PRO SHOP   ENTER START ROUND");
        }

        private static void BuildBriefing(Transform root, HudView view)
        {
            const float w = 1500, h = 214;
            view.Briefing = Card(root, "Briefing", w, h, 70, Cyan);
            var c = (RectTransform)view.Briefing.Root.transform;
            view.BriefHole = Label(c, "Hole", _display, 62, Cream, TextAnchor.MiddleLeft);
            Place(view.BriefHole.rectTransform, 38, 18, 270, 70);
            view.BriefHole.characterSpacing = 2f;
            view.BriefHole.text = "HOLE 3";
            view.BriefPar = Label(c, "Par", _bold, 29, Cyan, TextAnchor.MiddleLeft);
            Place(view.BriefPar.rectTransform, 40, 86, 270, 36);
            view.BriefPar.characterSpacing = 2f;
            view.BriefPar.text = "PAR 3  \u00b7  165 YD";
            Image d1 = Img(c, "Divider 1", _dividerV, RuleColor, false);
            Place(d1.rectTransform, 322, 20, 6, 112);

            TMP_Text planLabel = Label(c, "Plan Label", _semi, 19, Brass, TextAnchor.MiddleLeft);
            Place(planLabel.rectTransform, 352, 20, 300, 24);
            planLabel.characterSpacing = 6f;
            planLabel.text = "HOLE PLAN";
            view.BriefHazard = Label(c, "Hazard", _semi, 29, Cream, TextAnchor.MiddleLeft);
            Place(view.BriefHazard.rectTransform, 352, 46, 830, 38);
            AutoSize(view.BriefHazard, 20, 29);
            view.BriefHazard.text = "Water guards the left of the green; a bunker sits front right.";
            view.BriefPlan = Label(c, "Plan", _semi, 23, Cream.WithAlpha(0.72f), TextAnchor.MiddleLeft);
            Place(view.BriefPlan.rectTransform, 352, 88, 830, 32);
            AutoSize(view.BriefPlan, 16, 23);
            view.BriefPlan.text = "Aim at the centre of the green. Short and right leaves an easy chip.";

            Image d2 = Img(c, "Divider 2", _dividerV, RuleColor, false);
            Place(d2.rectTransform, 1204, 20, 6, 112);
            view.BriefWindArrow = Dial(c, 1232, 24, 100);
            TMP_Text windLabel = Label(c, "Wind Label", _semi, 18, Muted, TextAnchor.MiddleLeft);
            Place(windLabel.rectTransform, 1346, 34, 120, 22);
            windLabel.characterSpacing = 5f;
            windLabel.text = "WIND";
            view.BriefWind = Label(c, "Wind", _extra, 42, Cream, TextAnchor.MiddleLeft);
            Place(view.BriefWind.rectTransform, 1345, 56, 140, 52);
            view.BriefWind.text = "4<size=55%> MPH</size>";

            Image rule = Img(c, "Rule", _rule, RuleColor, false);
            Place(rule.rectTransform, 30, 140, w - 60, 6);
            RectTransform diff = Child(c, "Difficulty", 38, 160, 112, 32);
            Image diffLine = Img(diff, "Frame", _plateLine, Brass, true);
            Stretch(diffLine.rectTransform);
            view.BriefDifficulty = Label(diff, "Label", _bold, 18, Brass, TextAnchor.MiddleCenter);
            Stretch(view.BriefDifficulty.rectTransform);
            view.BriefDifficulty.characterSpacing = 3f;
            view.BriefDifficulty.text = "CLUB";
            view.BriefPlayer = Label(c, "Player", _bold, 23, Cream, TextAnchor.MiddleLeft);
            Place(view.BriefPlayer.rectTransform, 168, 158, 600, 36);
            view.BriefPlayer.characterSpacing = 2f;
            view.BriefPlayer.text = "<color=#2F6FD6>CALEB</color>  \u00b7  TEEING OFF";

            view.Briefing.Footer = HintRow(c, "Footer", 32f, _bold, 21f, Cream, false);
            Anchor(view.Briefing.Footer.Root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 18), view.Briefing.Footer.Root.sizeDelta);
            view.SetHints(view.Briefing.Footer, "ENTER TEE OFF   ESC PAUSE");
        }

        private static void BuildResult(Transform root, HudView view)
        {
            const float w = 960, h = 320;
            view.Result = Card(root, "Shot Result", w, h, 90, Palette.Player(0));
            var c = (RectTransform)view.Result.Root.transform;
            view.ResultMedal = Medallion(c, 62, 30, 116, 58, Palette.Player(0), "C");
            view.ResultPlayer = Label(c, "Player", _bold, 23, Cream, TextAnchor.MiddleCenter);
            Place(view.ResultPlayer.rectTransform, 10, 160, 220, 30);
            view.ResultPlayer.characterSpacing = 1.5f;
            view.ResultPlayer.text = "<color=#2F6FD6>CALEB</color>  \u00b7  SHOT 1";
            view.ResultInput = Label(c, "Input", _semi, 19, Cream.WithAlpha(0.75f), TextAnchor.MiddleCenter);
            Place(view.ResultInput.rectTransform, 10, 196, 220, 28);
            view.ResultInput.characterSpacing = 1f;
            view.ResultInput.text = "LANE <color=#00CBE7>11</color>   \u00b7   POWER <color=#00CBE7>75%</color>";
            Image d = Img(c, "Divider", _dividerV, RuleColor, false);
            Place(d.rectTransform, 240, 26, 6, 204);

            view.ResultLie = Img(c, "Lie", Lie("fairway"), Color.white, false);
            Place(view.ResultLie.rectTransform, 270, 30, 58, 58);
            view.ResultTitle = Label(c, "Title", _display, 66, Palette.Hex(0x8CC63F), TextAnchor.MiddleLeft);
            Place(view.ResultTitle.rectTransform, 342, 20, 590, 74);
            AutoSize(view.ResultTitle, 38, 66);
            view.ResultTitle.characterSpacing = 2f;
            view.ResultTitle.text = "FAIRWAY";
            view.ResultSubtitle = Label(c, "Subtitle", _bold, 25, Cream.WithAlpha(0.85f), TextAnchor.MiddleLeft);
            Place(view.ResultSubtitle.rectTransform, 344, 92, 590, 32);
            AutoSize(view.ResultSubtitle, 16, 25);
            view.ResultSubtitle.characterSpacing = 2f;
            view.ResultSubtitle.text = "28 YD TO PIN";

            view.ResultStats = new HudStat[3];
            view.ResultDividers = new GameObject[2];
            string[] statLabels = { "CARRY", "ROLL", "TOTAL" };
            string[] statValues = { "168", "12", "180" };
            for (int i = 0; i < 3; i++)
            {
                float x = 272 + i * 222;
                RectTransform stat = Child(c, $"Stat {statLabels[i]}", x, 140, 204, 86);
                var s = new HudStat { Root = stat.gameObject };
                s.Label = Label(stat, "Label", _semi, 18, Muted, TextAnchor.MiddleLeft);
                Place(s.Label.rectTransform, 0, 0, 204, 24);
                s.Label.characterSpacing = 4f;
                s.Label.text = statLabels[i];
                s.Value = Label(stat, "Value", _extra, 50, Cyan, TextAnchor.MiddleLeft);
                Place(s.Value.rectTransform, 0, 24, 204, 58);
                s.Value.text = statValues[i] + "<size=55%> YD</size>";
                view.ResultStats[i] = s;
                if (i == 0) continue;
                Image sd = Img(c, $"Stat Divider {i}", _dividerV, RuleColor, false);
                Place(sd.rectTransform, x - 16, 142, 6, 82);
                view.ResultDividers[i - 1] = sd.gameObject;
            }
            Footer(c, view, view.Result, w, "ENTER CONTINUE");
        }

        private static void BuildHandoff(Transform root, HudView view)
        {
            const float w = 780, h = 262;
            view.Handoff = Card(root, "Handoff", w, h, 110, Palette.Player(1));
            var c = (RectTransform)view.Handoff.Root.transform;
            FlankedTitle(c, "Next Up", w, 18, "NEXT UP", Brass, 20);
            view.HandoffMedal = Medallion(c, 150, 60, 104, 52, Palette.Player(1), "M");
            view.HandoffName = Label(c, "Name", _display, 66, Palette.Player(1), TextAnchor.MiddleLeft);
            Place(view.HandoffName.rectTransform, 278, 54, 460, 74);
            AutoSize(view.HandoffName, 40, 66);
            view.HandoffName.characterSpacing = 2f;
            view.HandoffName.text = "MAYA";
            view.HandoffStroke = Label(c, "Stroke", _bold, 23, Cream, TextAnchor.MiddleLeft);
            Place(view.HandoffStroke.rectTransform, 280, 126, 120, 32);
            view.HandoffStroke.characterSpacing = 2f;
            view.HandoffStroke.text = "STROKE 1";
            view.HandoffLieBadge = Img(c, "Lie Badge", Lie("tee_sm"), Color.white, false);
            Place(view.HandoffLieBadge.rectTransform, 396, 128, 28, 28);
            view.HandoffLie = Label(c, "Lie", _bold, 22, Cream.WithAlpha(0.85f), TextAnchor.MiddleLeft);
            Place(view.HandoffLie.rectTransform, 432, 126, 330, 32);
            AutoSize(view.HandoffLie, 15, 22);
            view.HandoffLie.characterSpacing = 1.5f;
            view.HandoffLie.text = "167 YD TO PIN  \u00b7  <color=#8CC63F>TEE</color>";
            Footer(c, view, view.Handoff, w, "ENTER READY   ESC PAUSE");
        }
    }
}
