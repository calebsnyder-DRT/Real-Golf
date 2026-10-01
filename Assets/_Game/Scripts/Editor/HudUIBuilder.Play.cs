using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>In-play corner chips, the lane strip, the power meter and the notice banner.</summary>
    public static partial class HudUIBuilder
    {
        private static void BuildPlayTop(Transform root, HudView view)
        {
            RectTransform top = Layer(root, "Play Top");
            view.PlayTop = top.gameObject;

            Image washL = Img(top, "Wash Left", _washCorner, Ink.WithAlpha(0.5f), false);
            Anchor(washL.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(760, 300));
            Image washR = Img(top, "Wash Right", _washCorner, Ink.WithAlpha(0.42f), false);
            Anchor(washR.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-280, 0), new Vector2(560, 240));
            washR.rectTransform.localScale = new Vector3(-1, 1, 1);

            // Hole / par / distance / stroke
            RectTransform hole = Corner(top, "Hole", new Vector2(0, 1), new Vector2(Margin, -30), new Vector2(372, 116));
            Panel(hole, _glass);
            Image block = Img(hole, "Number Block", _plate, Cyan, true);
            Place(block.rectTransform, 12, 12, 92, 92);
            TMP_Text holeLabel = Label(block.rectTransform, "Label", _bold, 17, Ink, TextAnchor.MiddleCenter);
            Place(holeLabel.rectTransform, 0, 6, 92, 22);
            holeLabel.characterSpacing = 5f;
            holeLabel.text = "HOLE";
            view.HoleNumber = Label(block.rectTransform, "Number", _display, 60, Ink, TextAnchor.MiddleCenter);
            Place(view.HoleNumber.rectTransform, 0, 22, 92, 68);
            view.HoleNumber.text = "3";
            view.HolePar = Label(hole, "Par", _extra, 40, Cream, TextAnchor.MiddleLeft);
            Place(view.HolePar.rectTransform, 120, 6, 140, 50);
            view.HolePar.text = "PAR 3";
            RectTransform diff = Child(hole, "Difficulty", 266, 16, 92, 28);
            Image diffLine = Img(diff, "Frame", _plateLine, Brass, true);
            Stretch(diffLine.rectTransform);
            view.HoleDifficulty = Label(diff, "Label", _bold, 17, Brass, TextAnchor.MiddleCenter);
            Stretch(view.HoleDifficulty.rectTransform);
            view.HoleDifficulty.characterSpacing = 3f;
            view.HoleDifficulty.text = "CLUB";
            view.HoleDistance = Label(hole, "Distance", _bold, 28, Cyan, TextAnchor.MiddleLeft);
            Place(view.HoleDistance.rectTransform, 120, 54, 240, 32);
            view.HoleDistance.text = "167 YD <size=70%><color=#F2EDDFB3>TO PIN</color></size>";
            view.HoleStroke = Label(hole, "Stroke", _semi, 21, Cream.WithAlpha(0.78f), TextAnchor.MiddleLeft);
            Place(view.HoleStroke.rectTransform, 121, 84, 240, 24);
            view.HoleStroke.characterSpacing = 3f;
            view.HoleStroke.text = "STROKE 1";

            // Roster: identity medallions over name plates
            const float slotW = 124f, slotH = 118f, gap = 10f;
            int count = HudView.MaxPlayers;
            RectTransform roster = Corner(top, "Roster", new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(count * slotW + (count - 1) * gap, slotH));
            var layout = roster.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            view.Roster = new HudRosterSlot[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform slot = Child(roster, $"Player {i + 1}", 0, 0, slotW, slotH);
                var s = new HudRosterSlot { Root = slot.gameObject };
                s.Medal = Medallion(slot, (slotW - 66) / 2, 0, 66, 34, Palette.Player(i), SampleNames[i].Substring(0, 1));
                s.Plate = Img(slot, "Plate", _plate, Palette.Hex(0x141A1A).WithAlpha(0.86f), true);
                Place(s.Plate.rectTransform, 2, 74, slotW - 4, 38);
                s.Underline = Img(s.Plate.rectTransform, "Identity", _soft, Palette.Player(i), true);
                Place(s.Underline.rectTransform, 8, 32, slotW - 20, 3);
                s.Name = Label(s.Plate.rectTransform, "Name", _bold, 20, Cream, TextAnchor.MiddleLeft);
                Place(s.Name.rectTransform, 10, 0, 76, 34);
                AutoSize(s.Name, 14, 20);
                s.Name.text = SampleNames[i];
                s.Score = Label(s.Plate.rectTransform, "Score", _extra, 23, Palette.Player(i), TextAnchor.MiddleRight);
                Place(s.Score.rectTransform, slotW - 4 - 46, 0, 38, 34);
                s.Score.text = "0";
                view.Roster[i] = s;
            }

            // Wind
            RectTransform wind = Corner(top, "Wind", new Vector2(1, 1), new Vector2(-Margin, -30), new Vector2(256, 104));
            Panel(wind, _glass);
            TMP_Text windLabel = Label(wind, "Label", _semi, 18, Muted, TextAnchor.MiddleLeft);
            Place(windLabel.rectTransform, 20, 14, 100, 22);
            windLabel.characterSpacing = 5f;
            windLabel.text = "WIND";
            view.WindValue = Label(wind, "Value", _extra, 46, Cream, TextAnchor.MiddleLeft);
            Place(view.WindValue.rectTransform, 19, 36, 140, 56);
            view.WindValue.text = "4<size=55%> MPH</size>";
            view.WindArrow = Dial(wind, 158, 8, 88);
        }

        private static void BuildPlayBottom(Transform root, HudView view)
        {
            RectTransform bottom = Layer(root, "Play Bottom");
            view.PlayBottom = bottom.gameObject;
            Image wash = Img(bottom, "Wash", _washBottom, Ink.WithAlpha(0.42f), false);
            wash.rectTransform.anchorMin = new Vector2(0, 0);
            wash.rectTransform.anchorMax = new Vector2(1, 0);
            wash.rectTransform.pivot = new Vector2(0.5f, 0);
            wash.rectTransform.anchoredPosition = Vector2.zero;
            wash.rectTransform.sizeDelta = new Vector2(0, 280);

            // Club + lie
            RectTransform club = Corner(bottom, "Club", new Vector2(0, 0), new Vector2(Margin, Margin), new Vector2(390, 170));
            Panel(club, _glass);
            Image well = Img(club, "Icon Well", _plate, Color.white.WithAlpha(0.05f), true);
            Place(well.rectTransform, 12, 12, 146, 146);
            view.ClubIcon = Img(club, "Icon", Icon("club.iron5"), Color.white, false);
            view.ClubIcon.preserveAspect = true;
            Place(view.ClubIcon.rectTransform, 14, 14, 142, 142);
            view.ClubName = Label(club, "Name", _extra, 40, Cream, TextAnchor.MiddleLeft);
            Place(view.ClubName.rectTransform, 172, 12, 206, 48);
            AutoSize(view.ClubName, 24, 40);
            view.ClubName.text = "5 IRON";
            view.ClubRange = Label(club, "Range", _bold, 27, Cyan, TextAnchor.MiddleLeft);
            Place(view.ClubRange.rectTransform, 173, 58, 206, 34);
            AutoSize(view.ClubRange, 18, 27);
            view.ClubRange.text = "150\u2013200 YD";
            Image div = Img(club, "Divider", null, RuleColor, false);
            Place(div.rectTransform, 174, 104, 200, 1.5f);
            view.LieBadge = Img(club, "Lie Badge", Lie("tee_sm"), Color.white, false);
            Place(view.LieBadge.rectTransform, 172, 116, 38, 38);
            view.LieText = Label(club, "Lie", _bold, 25, Palette.Hex(0x8CC63F), TextAnchor.MiddleLeft);
            Place(view.LieText.rectTransform, 220, 116, 158, 38);
            view.LieText.characterSpacing = 2f;
            view.LieText.text = "TEE";

            // 1-20 lane strip
            const int lanes = 20;
            const float keyW = 46f, keyH = 54f, keyGap = 5f, pad = 12f, railH = 74f;
            float stripW = lanes * keyW + (lanes - 1) * keyGap + pad * 2;
            RectTransform strip = Corner(bottom, "Lanes", new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(stripW, railH + 22));
            Image rail = Img(strip, "Rail", _glass, Color.white.WithAlpha(0.72f), true);
            Place(rail.rectTransform, 0, 0, stripW, railH);
            view.Lanes = new HudLane[lanes];
            for (int i = 0; i < lanes; i++)
            {
                RectTransform cell = Child(strip, $"Lane {i + 1}", pad + i * (keyW + keyGap), 10, keyW, keyH);
                var lane = new HudLane { Root = cell };
                lane.Glow = Img(cell, "Glow", _laneGlow, Cyan, true);
                Inset(lane.Glow.rectTransform, -24);
                lane.Glow.enabled = false;
                bool centre = i == 9 || i == 10;
                lane.Key = Img(cell, "Key", _laneKey, centre ? Palette.Hex(0x3D4B4B) : Palette.Hex(0x2A3434), true);
                Stretch(lane.Key.rectTransform);
                lane.Frame = Img(cell, "Frame", _laneFrame, Brass.WithAlpha(0.75f), true);
                Stretch(lane.Frame.rectTransform);
                lane.Frame.enabled = centre;
                lane.Number = Label(cell, "Number", _bold, 25, Cream.WithAlpha(centre ? 1f : 0.82f), TextAnchor.MiddleCenter);
                Place(lane.Number.rectTransform, 0, 0, keyW, keyH - 6);
                lane.Number.characterSpacing = 0f;
                lane.Number.text = (i + 1).ToString();
                view.Lanes[i] = lane;
            }
            RectTransform pointer = Child(strip, "Detected Pointer", pad + 10 * (keyW + keyGap), railH + 2, keyW, 18);
            Image tri = Img(pointer, "Arrow", _lanePointer, Cyan, false);
            Place(tri.rectTransform, (keyW - 26) / 2, 0, 26, 18);
            view.LanePointer = pointer;
            pointer.gameObject.SetActive(false);

            // Power meter
            RectTransform power = Corner(bottom, "Power", new Vector2(1, 0), new Vector2(-Margin, Margin), new Vector2(186, 386));
            Panel(power, _glass);
            TMP_Text powerLabel = Label(power, "Label", _semi, 19, Muted, TextAnchor.MiddleLeft);
            Place(powerLabel.rectTransform, 18, 18, 90, 24);
            powerLabel.characterSpacing = 4f;
            powerLabel.text = "POWER";
            view.PowerValue = Label(power, "Value", _extra, 48, Cream, TextAnchor.MiddleRight);
            Place(view.PowerValue.rectTransform, 64, 4, 106, 54);
            view.PowerValue.text = "75<size=60%>%</size>";
            const float trackX = 42f, trackY = 66f, fillTop = trackY + 8f, fillH = 244f;
            Image track = Img(power, "Track", _meterTrack, Color.white, false);
            Place(track.rectTransform, trackX, trackY, 40, 260);
            view.PowerFill = Img(power, "Fill", _meterFill, Cyan, false);
            Place(view.PowerFill.rectTransform, trackX + 8, fillTop, 24, fillH);
            view.PowerFill.type = Image.Type.Filled;
            view.PowerFill.fillMethod = Image.FillMethod.Vertical;
            view.PowerFill.fillOrigin = (int)Image.OriginVertical.Bottom;
            view.PowerFill.fillAmount = 0.75f;
            Image ticks = Img(power, "Ticks", _meterTicks, Cream.WithAlpha(0.55f), false);
            Place(ticks.rectTransform, trackX + 48, fillTop - 2, 30, 248);
            foreach (int v in new[] { 100, 75, 50, 25 })
            {
                float y = fillTop + fillH * (1f - v / 100f);
                TMP_Text scale = Label(power, $"Scale {v}", _semi, 17, Cream.WithAlpha(0.62f), TextAnchor.MiddleLeft);
                Place(scale.rectTransform, trackX + 84, y - 11, 56, 22);
                scale.characterSpacing = 0.5f;
                scale.text = v + "%";
            }
            const float pointerSize = 24f;
            view.PowerPointer = Child(power, "Pointer", 12, fillTop + fillH * 0.25f - pointerSize / 2, pointerSize, pointerSize);
            Image pointerImg = Img(view.PowerPointer, "Arrow", _meterPointer, Cyan, false);
            Stretch(pointerImg.rectTransform);
            view.PowerPointerMinY = -(fillTop + fillH - pointerSize / 2);
            view.PowerPointerMaxY = -(fillTop - pointerSize / 2);
            view.StatusPill = Img(power, "Status", _pill, Palette.MutedGreenGray, true);
            Place(view.StatusPill.rectTransform, 14, 338, 158, 34);
            view.StatusText = Label(view.StatusPill.rectTransform, "Text", _bold, 19, Cream, TextAnchor.MiddleCenter);
            Stretch(view.StatusText.rectTransform);
            view.StatusText.characterSpacing = 2.5f;
            view.StatusText.text = "SET UP";
        }

        private static void BuildBanner(Transform root, HudView view)
        {
            RectTransform banner = Corner(root, "Banner", new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(780, 58));
            Panel(banner, _glass);
            Image block = Img(banner, "Alert Block", _plate, Palette.Warning, true);
            Place(block.rectTransform, 0, 0, 58, 58);
            Image icon = Img(block.rectTransform, "Icon", _alert, Ink, false);
            Place(icon.rectTransform, 13, 12, 32, 32);
            view.BannerText = Label(banner, "Text", _bold, 23, Cream, TextAnchor.MiddleLeft);
            Place(view.BannerText.rectTransform, 74, 0, 690, 58);
            AutoSize(view.BannerText, 15, 23);
            view.BannerText.text = "Recovered: your shot result was restored.";
            view.Banner = banner.gameObject;
        }
    }
}
