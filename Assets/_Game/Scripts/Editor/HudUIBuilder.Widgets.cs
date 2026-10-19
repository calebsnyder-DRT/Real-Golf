using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Shared widgets of the brand HUD builder. Every element is a flat 9-sliced shape, a circle, a ring or text, tinted
    /// from <see cref="HudBrand"/>: rounded fills take their radius from Image.pixelsPerUnitMultiplier, so one sprite
    /// serves every corner size and becomes a pill or a circle once the radius passes half the height.
    /// </summary>
    public static partial class HudUIBuilder
    {
        internal const float RoundSourceRadius = 128f;
        internal const float LineSourceScale = 2f;
        internal const float Pill = 9999f;
        internal static readonly string[] SampleNames = { "CALEB", "MAYA", "JORDAN", "SAM", "NIA", "LEO" };

        internal static readonly Color Midnight = HudBrand.Midnight;
        internal static readonly Color White = HudBrand.White;
        internal static readonly Color Muted = HudBrand.Muted;
        internal static readonly Color LineColor = HudBrand.Line;

        internal sealed class BadgeStyle
        {
            public float Height, Disc, DiscFont, NameSize, ExtraSize, ExtraTracking;
            public TMP_FontAsset ExtraFont;
            public int PadLeft = 8, PadRight = 24;
            public float Spacing = 12f;
            public Color Fill;
        }

        internal sealed class HintStyle
        {
            public float KeyHeight = 34f, LegendSize = 18f, LabelSize = 26f, Spacing = 12f, Gap = 20f;
            public Sprite Key;
            public Color KeyColor, LegendColor, LabelColor;
            public TMP_FontAsset LabelFont;
            public Color? Background;
            public RectOffset Padding = new RectOffset(0, 0, 0, 0);
        }

        /// <summary>Keycap + label hints on Midnight or over the scene: outlined key, Ringside Bold label.</summary>
        internal static HintStyle PlainHints(float labelSize = 26f, float keyHeight = 34f) => new HintStyle
        {
            KeyHeight = keyHeight, LabelSize = labelSize, LegendSize = keyHeight * 0.53f, Key = _lineR10,
            KeyColor = White.WithAlpha(0.5f), LegendColor = White, LabelColor = White, LabelFont = _bold, Gap = 20f,
        };

        /// <summary>The primary action: a Pink pill holding a pill keycap and a Midnight label.</summary>
        internal static HintStyle ButtonHints(float labelSize = 32f, float keyHeight = 40f, int pad = 12) => new HintStyle
        {
            KeyHeight = keyHeight, LabelSize = labelSize, LegendSize = keyHeight * 0.5f, Key = _linePill, Spacing = 16f,
            KeyColor = Midnight.WithAlpha(0.5f), LegendColor = Midnight, LabelColor = Midnight, LabelFont = _bold,
            Background = HudBrand.Pink, Padding = new RectOffset(pad, 36, pad, pad),
        };

        // ---------------------------------------------------------------- Shapes

        internal static Image Round(Transform parent, string name, Color color, float radius)
        {
            Image img = Img(parent, name, _round, color, true);
            img.pixelsPerUnitMultiplier = RoundSourceRadius / Mathf.Max(1f, radius);
            return img;
        }

        /// <summary>
        /// UGUI clamps each axis of a 9-slice border separately, so an oversized radius turns a pill into an ellipse. Once
        /// the hierarchy exists, every <see cref="Pill"/> fill gets the exact radius of half its shorter side (its fixed
        /// layout height, or its rect).
        /// </summary>
        internal static void FinishPills(GameObject root)
        {
            float marker = RoundSourceRadius / Pill;
            foreach (Image img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite != _round || !Mathf.Approximately(img.pixelsPerUnitMultiplier, marker)) continue;
                Rect r = img.rectTransform.rect;
                var le = img.GetComponent<LayoutElement>();
                float h = le != null && le.preferredHeight > 0 ? le.preferredHeight : r.height;
                float w = le != null && le.preferredWidth > 0 ? le.preferredWidth : r.width > 1 ? r.width : h;
                float radius = Mathf.Max(1f, Mathf.Min(w, h) / 2f);
                img.pixelsPerUnitMultiplier = RoundSourceRadius / radius;
            }
        }

        internal static Image Outline(Transform parent, string name, Sprite sprite, Color color)
        {
            Image img = Img(parent, name, sprite, color, true);
            img.pixelsPerUnitMultiplier = LineSourceScale;
            return img;
        }

        internal static Image Solid(Transform parent, string name, Color color) => Img(parent, name, null, color, false);

        internal static Image Dim(Transform parent, float alpha)
        {
            Image dim = Solid(parent, "Dim", Midnight.WithAlpha(alpha));
            Stretch(dim.rectTransform);
            return dim;
        }

        internal static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        // ---------------------------------------------------------------- Text

        /// <summary>A text label; <paramref name="tracking"/> is CSS letter-spacing in em.</summary>
        internal static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float size, Color color, string text,
            float tracking = 0f, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.characterSpacing = tracking * 100f;
            t.raycastTarget = false;
            t.richText = true;
            t.text = text;
            t.rectTransform.sizeDelta = new Vector2(t.GetPreferredValues(text).x, size * 1.2f);
            return t;
        }

        /// <summary>Positioned text from the parent's top-left corner, <paramref name="h"/> tall.</summary>
        internal static TMP_Text TextAt(Transform parent, string name, TMP_FontAsset font, float size, Color color, string text,
            float x, float y, float w, float h, float tracking = 0f, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t = Text(parent, name, font, size, color, text, tracking, align);
            Place(t.rectTransform, x, y, w, h);
            return t;
        }

        internal static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAnchor align)
        {
            TMP_Text t = Text(parent, name, font, size, color, "", 0.015f, Align(align));
            return t;
        }

        internal static TextAlignmentOptions Align(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.BottomRight;
            }
        }

        internal static void Wrap(TMP_Text t, float min)
        {
            t.textWrappingMode = TextWrappingModes.Normal;
            AutoSize(t, min, t.fontSize);
        }

        internal static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }

        /// <summary>The design's "twist": lowercase Bold Italic celebration words.</summary>
        internal static TMP_Text Twist(Transform parent, string name, float size, string text) =>
            Text(parent, name, _twist, size, White, text, -0.02f, TextAlignmentOptions.Midline);

        // ---------------------------------------------------------------- Layout

        internal static HorizontalLayoutGroup HStack(GameObject go, float spacing, RectOffset padding, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        internal static RectTransform Row(Transform parent, string name, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            HStack(rt.gameObject, spacing, padding, align);
            return rt;
        }

        internal static void Fit(GameObject go, bool horizontal = true, bool vertical = false)
        {
            var f = go.AddComponent<ContentSizeFitter>();
            f.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        internal static LayoutElement Size(Component c, float w, float h)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.minWidth = le.preferredWidth = w;
            if (h >= 0) le.minHeight = le.preferredHeight = h;
            ((RectTransform)c.transform).sizeDelta = new Vector2(Mathf.Max(0, w), Mathf.Max(0, h));
            return le;
        }

        internal static Image Divider(Transform parent, float h = 32f)
        {
            Image d = Solid(parent, "Divider", White.WithAlpha(0.2f));
            Size(d, 1, h);
            return d;
        }

        // ---------------------------------------------------------------- Composite widgets

        /// <summary>A player pill sized to its content. <paramref name="fit"/> when it is not inside another layout group.</summary>
        internal static HudBadge Badge(Transform parent, string name, BadgeStyle s, string sample, string extra, bool fit)
        {
            var b = new HudBadge();
            b.Fill = Round(parent, name, s.Fill, Pill);
            b.Root = b.Fill.gameObject;
            HStack(b.Root, s.Spacing, new RectOffset(s.PadLeft, s.PadRight, 0, 0), TextAnchor.MiddleLeft);
            Size(b.Fill, -1, s.Height);
            if (fit) Fit(b.Root);
            b.Fill.rectTransform.sizeDelta = new Vector2(300, s.Height);

            b.Disc = Round(b.Root.transform, "Disc", Midnight, Pill);
            Size(b.Disc, s.Disc, s.Disc);
            b.Initial = Text(b.Disc.transform, "Initial", _wide, s.DiscFont, White, sample.Substring(0, 1), 0f, TextAlignmentOptions.Midline);
            Stretch(b.Initial.rectTransform);
            b.Name = Text(b.Root.transform, "Name", _bold, s.NameSize, Midnight, sample);
            if (s.ExtraSize > 0)
                b.Extra = Text(b.Root.transform, "Extra", s.ExtraFont, s.ExtraSize, Midnight, extra, s.ExtraTracking);
            return b;
        }

        /// <summary>An identity circle; small discs take the heavier ring so it stays about 3 px.</summary>
        internal static HudDisc Disc(Transform parent, string name, float size, float fontSize, Color fill, string initial)
        {
            var d = new HudDisc();
            RectTransform rt = Corner(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            d.Root = rt.gameObject;
            d.Fill = Round(rt, "Fill", fill, Pill);
            Stretch(d.Fill.rectTransform);
            d.Ring = Img(rt, "Ring", size < 90 ? _ringMid : _ringThin, White.WithAlpha(0.22f), false);
            Stretch(d.Ring.rectTransform);
            d.Ring.enabled = false;
            d.Initial = Text(rt, "Initial", _wide, fontSize, HudBrand.TextOn(fill), initial, 0f, TextAlignmentOptions.Midline);
            Stretch(d.Initial.rectTransform);
            return d;
        }

        /// <summary>A pill sized to its text; <paramref name="line"/> draws it as an outline instead of a fill.</summary>
        internal static HudChip Chip(Transform parent, string name, Color fill, bool line, TMP_FontAsset font, float size, Color textColor,
            string text, float tracking, int padX, float height, bool fit)
        {
            var c = new HudChip();
            c.Fill = line ? Outline(parent, name, _linePill, fill) : Round(parent, name, fill, Pill);
            c.Root = c.Fill.gameObject;
            HStack(c.Root, 0, new RectOffset(padX, padX, 0, 0));
            Size(c.Fill, -1, height);
            if (fit) Fit(c.Root);
            c.Text = Text(c.Root.transform, "Text", font, size, textColor, text, tracking, TextAlignmentOptions.Midline);
            return c;
        }

        internal static HudHintRow HintRow(Transform parent, string name, HintStyle s, int keys = 6, int labels = 6, int gaps = 4)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            float height = s.KeyHeight + s.Padding.vertical;
            rt.sizeDelta = new Vector2(400, height);
            if (s.Background.HasValue)
            {
                Image bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = _round;
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = RoundSourceRadius / Pill;
                bg.color = s.Background.Value;
                bg.raycastTarget = false;
            }
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = s.Spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.padding = s.Padding;
            Fit(rt.gameObject);

            var row = new HudHintRow { Root = rt, KeyHeight = s.KeyHeight };
            row.Keys = new Image[keys];
            row.KeyLayouts = new LayoutElement[keys];
            row.Legends = new TMP_Text[keys];
            for (int i = 0; i < keys; i++)
            {
                Image key = Outline(rt, $"Key {i + 1}", s.Key, s.KeyColor);
                key.rectTransform.sizeDelta = new Vector2(s.KeyHeight, s.KeyHeight);
                row.KeyLayouts[i] = key.gameObject.AddComponent<LayoutElement>();
                row.KeyLayouts[i].preferredWidth = s.KeyHeight;
                TMP_Text legend = Text(key.rectTransform, "Legend", _condensedBold, s.LegendSize, s.LegendColor, "", 0.1f, TextAlignmentOptions.Midline);
                Stretch(legend.rectTransform);
                row.Keys[i] = key;
                row.Legends[i] = legend;
            }
            row.Labels = new TMP_Text[labels];
            for (int i = 0; i < labels; i++)
            {
                TMP_Text t = Text(rt, $"Label {i + 1}", s.LabelFont, s.LabelSize, s.LabelColor, "");
                t.rectTransform.sizeDelta = new Vector2(60, s.KeyHeight);
                row.Labels[i] = t;
            }
            row.Gaps = new GameObject[gaps];
            for (int i = 0; i < gaps; i++)
            {
                var gap = new GameObject($"Gap {i + 1}", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(rt, false);
                ((RectTransform)gap.transform).sizeDelta = new Vector2(s.Gap, s.KeyHeight);
                gap.GetComponent<LayoutElement>().preferredWidth = s.Gap;
                row.Gaps[i] = gap;
            }
            return row;
        }

        /// <summary>
        /// Hint row anchored at <paramref name="anchor"/> and pivoted at <paramref name="pivot"/> (default: the anchor), so
        /// a right pivot grows the row leftwards; laid out with <paramref name="hints"/>.
        /// </summary>
        internal static HudHintRow Hints(Transform parent, HudView view, string name, HintStyle s, string hints, Vector2 anchor, Vector2 pos, Vector2? pivot = null)
        {
            HudHintRow row = HintRow(parent, name, s);
            Anchor(row.Root, anchor, pivot ?? anchor, pos, row.Root.sizeDelta);
            view.SetHints(row, hints);
            return row;
        }

        internal static HudMotion Motion(Component c, HudMotionKind kind, float delay, float duration, float baseAlpha = 1f)
        {
            var m = c.gameObject.AddComponent<HudMotion>();
            m.Kind = kind;
            m.Delay = delay;
            m.Duration = duration;
            m.BaseAlpha = baseAlpha;
            return m;
        }

        /// <summary>Five concentric bands radiating from <paramref name="centre"/> (reference px from the top-left), staggered 0.36 s.</summary>
        internal static GameObject Rings(Transform parent, string name, Color[] colors, Vector2 centre, float size, float duration)
        {
            RectTransform group = Layer(parent, name);
            for (int i = 0; i < colors.Length; i++)
            {
                Image ring = Img(group, $"Ring {i + 1}", _ringBand, colors[i], false);
                Anchor(ring.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(centre.x, -centre.y), new Vector2(size, size));
                Motion(ring, HudMotionKind.Ring, i * 0.36f, duration);
            }
            return group.gameObject;
        }

        // ---------------------------------------------------------------- Rect helpers

        internal static RectTransform Layer(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);
            return rt;
        }

        /// <summary>Child anchored and pivoted at <paramref name="anchor"/>, offset by <paramref name="pos"/> reference pixels.</summary>
        internal static RectTransform Corner(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Anchor(rt, anchor, anchor, pos, size);
            return rt;
        }

        internal static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Child positioned from its parent's top-left corner.</summary>
        internal static RectTransform Child(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);
            return rt;
        }

        internal static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Full-width strip <paramref name="h"/> tall, <paramref name="y"/> below the parent's top edge.</summary>
        internal static void HStretch(RectTransform rt, float y, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -y);
            rt.sizeDelta = new Vector2(0, h);
        }

        internal static void Stretch(RectTransform rt) => Inset(rt, 0);

        internal static void Inset(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
