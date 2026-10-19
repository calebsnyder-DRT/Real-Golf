using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>The in-shot rail (design 1b), the control hints above it and the notice banner.</summary>
    public static partial class HudUIBuilder
    {
        private const float RailBottom = 36f, RailHeight = 76f;

        private static void BuildRail(Transform root, HudView view)
        {
            Image rail = Round(root, "Rail", Midnight.WithAlpha(0.78f), Pill);
            RectTransform rt = rail.rectTransform;
            Anchor(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, RailBottom), new Vector2(1400, RailHeight));
            HStack(rail.gameObject, 28, new RectOffset(12, 32, 0, 0));
            Fit(rail.gameObject);
            view.Rail = rail.gameObject;

            view.RailPlayer = Badge(rt, "Player", new BadgeStyle
            {
                Height = 56, Disc = 40, DiscFont = 20, NameSize = 28, Fill = HudBrand.Aqua,
                ExtraFont = _condensedBold, ExtraSize = 22, ExtraTracking = 0.1f,
            }, "CALEB", "STROKE 1", false);

            RectTransform hole = Row(rt, "Hole", 10);
            view.RailHole = Text(hole, "Hole", _condensed, 22, Muted, "HOLE 3", 0.12f);
            view.RailDistance = Text(hole, "Distance", _wide, 30, White, "165<size=60%> YD</size>");
            view.RailPar = Text(hole, "Par", _condensed, 22, Muted, "PAR 3", 0.12f);

            Divider(rt);
            view.RailClub = Text(rt, "Club", _bold, 28, White, "5 IRON");
            Divider(rt);

            RectTransform wind = Row(rt, "Wind", 8);
            RectTransform well = Child(wind, "Arrow Well", 0, 0, 28, 28);
            Size(well, 28, 28);
            Image arrow = Img(well, "Arrow", _arrow, HudBrand.Aqua, false);
            Stretch(arrow.rectTransform);
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -135);
            view.WindArrow = arrow.rectTransform;
            view.WindValue = Text(wind, "Value", _wide, 30, White, "4<size=60%> MPH</size>");
            Divider(rt);

            RectTransform power = Row(rt, "Power", 14);
            RectTransform bar = Child(power, "Bar", 0, 0, 220, 10);
            Size(bar, 220, 10);
            Image track = Round(bar, "Track", White.WithAlpha(0.18f), Pill);
            Stretch(track.rectTransform);
            Image fill = Round(bar, "Fill", HudBrand.Aqua, Pill);
            RectTransform f = fill.rectTransform;
            f.anchorMin = Vector2.zero;
            f.anchorMax = new Vector2(0.36f, 1f);
            f.pivot = new Vector2(0, 0.5f);
            f.offsetMin = f.offsetMax = Vector2.zero;
            view.PowerFill = f;
            view.PowerValue = Text(power, "Value", _wide, 30, White, "36<size=60%>%</size>");
            view.ReadyDot = Round(power, "Ready", HudBrand.Aqua, Pill);
            Size(view.ReadyDot, 14, 14);
            view.ReadyPulse = Motion(view.ReadyDot, HudMotionKind.Pulse, 0, 1.6f);
        }

        private static void BuildPlayHints(Transform root, HudView view)
        {
            HintStyle s = PlainHints(22, 30);
            s.Background = Midnight.WithAlpha(0.66f);
            s.Padding = new RectOffset(10, 22, 6, 6);
            view.PlayHints = Hints(root, view, "Hints", s, "←→ aim   ↑↓ club   ENTER ready",
                new Vector2(0.5f, 0), new Vector2(0, RailBottom + RailHeight + 16));
        }

        private static void BuildBanner(Transform root, HudView view)
        {
            Image banner = Round(root, "Banner", Midnight.WithAlpha(0.86f), Pill);
            Anchor(banner.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(800, 60));
            HStack(banner.gameObject, 16, new RectOffset(12, 30, 0, 0), TextAnchor.MiddleLeft);
            Fit(banner.gameObject);
            Image dot = Round(banner.transform, "Alert", HudBrand.Sun, Pill);
            Size(dot, 40, 40);
            TMP_Text mark = Text(dot.transform, "Mark", _wide, 22, Midnight, "!", 0f, TextAlignmentOptions.Midline);
            Stretch(mark.rectTransform);
            view.BannerText = Text(banner.transform, "Text", _bold, 24, White, "Recovered: your shot result was restored.");
            view.Banner = banner.gameObject;
        }
    }
}
