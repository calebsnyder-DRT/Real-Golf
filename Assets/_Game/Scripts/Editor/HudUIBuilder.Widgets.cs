using System.Collections.Generic;
using Puttshack.Runtime;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>Shared widgets of the HUD builder: cards, medallions, dials, keycap hint rows and layout helpers.</summary>
    public static partial class HudUIBuilder
    {
        private const float Margin = 36f;
        private static readonly string[] SampleNames = { "CALEB", "MAYA", "JORDAN", "SAM", "NIA", "LEO" };

        private static readonly Color Ink = Palette.Ink;
        private static readonly Color Cream = Palette.Cream;
        private static readonly Color Cyan = Palette.Cyan;
        private static readonly Color Brass = HudView.Brass;
        private static readonly Color Leaf = HudView.Leaf;
        private static readonly Color Gold = Palette.Hex(0xD9B65C);
        private static readonly Color Muted = Palette.Cream.WithAlpha(0.62f);
        private static readonly Color RuleColor = Palette.Cream.WithAlpha(0.2f);

        private static HudCard Card(Transform parent, string name, float w, float h, float bottom, Color accent)
        {
            RectTransform rt = Corner(parent, name, new Vector2(0.5f, 0), new Vector2(0, bottom), new Vector2(w, h));
            var card = new HudCard { Root = rt.gameObject };
            Image frame = Img(rt, "Frame", _card, Color.white, true);
            Stretch(frame.rectTransform);
            card.Accent = Img(rt, "Accent", _accent, accent, true);
            Stretch(card.Accent.rectTransform);
            return card;
        }

        private static void Footer(RectTransform card, HudView view, HudCard target, float w, string hints)
        {
            Image rule = Img(card, "Footer Rule", _rule, RuleColor, false);
            Anchor(rule.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 64), new Vector2(w - 60, 6));
            target.Footer = HintRow(card, "Footer", 32f, _bold, 21f, Cream, false);
            Anchor(target.Footer.Root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), target.Footer.Root.sizeDelta);
            view.SetHints(target.Footer, hints);
        }

        /// <summary>Centred spaced title with fading rules either side; the rules hug the baked sample text.</summary>
        private static TMP_Text FlankedTitle(RectTransform parent, string name, float w, float y, string text, Color color, float size)
        {
            float lineH = size + 10;
            TMP_Text t = Label(parent, name, _bold, size, color, TextAnchor.MiddleCenter);
            Place(t.rectTransform, w / 2 - 260, y, 520, lineH);
            t.characterSpacing = 6f;
            t.text = text;
            float half = t.GetPreferredValues(text).x / 2 + 22;
            const float ruleW = 150f;
            Image left = Img(parent, name + " Rule L", _ruleL, color.WithAlpha(0.85f), false);
            Place(left.rectTransform, w / 2 - half - ruleW, y + lineH / 2 - 3, ruleW, 6);
            Image right = Img(parent, name + " Rule R", _ruleL, color.WithAlpha(0.85f), false);
            Place(right.rectTransform, w / 2 + half, y + lineH / 2 - 3, ruleW, 6);
            right.rectTransform.pivot = new Vector2(0.5f, 1);
            right.rectTransform.anchoredPosition += new Vector2(ruleW / 2, 0);
            right.rectTransform.localScale = new Vector3(-1, 1, 1);
            return t;
        }

        private static HudMedallion Medallion(RectTransform parent, float x, float y, float size, float fontSize, Color identity, string initial)
        {
            RectTransform rt = Child(parent, "Medallion", x, y, size, size);
            var m = new HudMedallion { Root = rt.gameObject };
            float g = size * 1.5f;
            m.Glow = Img(rt, "Glow", _ringGlow, identity, false);
            Place(m.Glow.rectTransform, (size - g) / 2, (size - g) / 2, g, g);
            m.Glow.enabled = false;
            Image disc = Img(rt, "Disc", _disc, Color.white, false);
            Stretch(disc.rectTransform);
            m.Ring = Img(rt, "Ring", _ring, identity, false);
            Stretch(m.Ring.rectTransform);
            m.Initial = Label(rt, "Initial", _display, fontSize, Cream, TextAnchor.MiddleCenter);
            Stretch(m.Initial.rectTransform);
            m.Initial.characterSpacing = 0f;
            m.Initial.text = initial;
            float b = Mathf.Round(size * 0.38f);
            m.Check = Img(rt, "Holed", _badgeCheck, Color.white, false);
            Place(m.Check.rectTransform, size - b * 0.8f, size - b * 0.9f, b, b);
            m.Check.enabled = false;
            return m;
        }

        private static RectTransform Dial(RectTransform parent, float x, float y, float size)
        {
            Image dial = Img(parent, "Wind Dial", _windDial, Color.white, false);
            Place(dial.rectTransform, x, y, size, size);
            Image arrow = Img(dial.rectTransform, "Arrow", _windArrow, Cyan, false);
            RectTransform a = arrow.rectTransform;
            Anchor(a, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.56f, size * 0.56f));
            a.localRotation = Quaternion.Euler(0, 0, -35);
            return a;
        }

        private static void SectionHeader(RectTransform parent, string name, float x, float y, float width, Sprite icon, string text, params string[] keys)
        {
            RectTransform rt = Child(parent, name, x, y, width, 30);
            Image ic = Img(rt, "Icon", icon, Brass, false);
            Place(ic.rectTransform, 0, 1, 28, 28);
            TMP_Text t = Label(rt, "Label", _semi, 20, Brass, TextAnchor.MiddleLeft);
            Place(t.rectTransform, 36, 0, 200, 30);
            t.characterSpacing = 5f;
            t.text = text;
            float cursor = width;
            for (int i = keys.Length - 1; i >= 0; i--)
            {
                Sprite s = KeySprites[keys[i]];
                float kw = 30f * s.rect.width / s.rect.height;
                cursor -= kw;
                Image k = Img(rt, "Key " + keys[i], s, Color.white, false);
                Place(k.rectTransform, cursor, 0, kw, 30);
                cursor -= 6;
            }
        }

        private static HudHintRow HintRow(Transform parent, string name, float keyHeight, TMP_FontAsset font, float fontSize, Color labelColor, bool background,
            int keys = 8, int labels = 8, int gaps = 4)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            float pad = background ? 6f : 0f;
            rt.sizeDelta = new Vector2(400, keyHeight + pad * 2);
            if (background)
            {
                var bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = _pill;
                bg.type = Image.Type.Sliced;
                bg.color = Ink.WithAlpha(0.66f);
                bg.raycastTarget = false;
            }
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.padding = background ? new RectOffset(12, 18, (int)pad, (int)pad) : new RectOffset(0, 0, 0, 0);
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var row = new HudHintRow { Root = rt, KeyHeight = keyHeight };
            row.Keys = new Image[keys];
            row.KeyLayouts = new LayoutElement[keys];
            row.Legends = new TMP_Text[keys];
            for (int i = 0; i < keys; i++)
            {
                Image key = Img(rt, $"Key {i + 1}", _keyBlank, Color.white, true);
                key.rectTransform.sizeDelta = new Vector2(keyHeight, keyHeight);
                row.KeyLayouts[i] = key.gameObject.AddComponent<LayoutElement>();
                row.KeyLayouts[i].preferredWidth = keyHeight;
                TMP_Text legend = Label(key.rectTransform, "Legend", _bold, keyHeight * 0.5f, Cream, TextAnchor.MiddleCenter);
                Stretch(legend.rectTransform);
                legend.rectTransform.offsetMin = new Vector2(0, keyHeight * 0.1f);
                legend.characterSpacing = 1f;
                legend.enabled = false;
                row.Keys[i] = key;
                row.Legends[i] = legend;
            }
            row.Labels = new TMP_Text[labels];
            for (int i = 0; i < labels; i++)
            {
                TMP_Text t = Label(rt, $"Label {i + 1}", font, fontSize, labelColor, TextAnchor.MiddleLeft);
                t.rectTransform.sizeDelta = new Vector2(60, keyHeight);
                t.characterSpacing = 2f;
                row.Labels[i] = t;
            }
            row.Gaps = new GameObject[gaps];
            for (int i = 0; i < gaps; i++)
            {
                var gap = new GameObject($"Gap {i + 1}", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(rt, false);
                ((RectTransform)gap.transform).sizeDelta = new Vector2(16, keyHeight);
                gap.GetComponent<LayoutElement>().preferredWidth = 16;
                row.Gaps[i] = gap;
            }
            return row;
        }

        private static void Panel(RectTransform rt, Sprite sprite)
        {
            Image f = Img(rt, "Panel", sprite, Color.white, true);
            Stretch(f.rectTransform);
            f.transform.SetAsFirstSibling();
        }

        private static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced)
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

        private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = Align(align);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.characterSpacing = 1.5f;
            text.raycastTarget = false;
            text.richText = true;
            return text;
        }

        private static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }

        private static TextAlignmentOptions Align(TextAnchor anchor)
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

        private static RectTransform Layer(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);
            return rt;
        }

        /// <summary>Child anchored and pivoted at <paramref name="anchor"/>, offset by <paramref name="pos"/> reference pixels.</summary>
        private static RectTransform Corner(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Anchor(rt, anchor, anchor, pos, size);
            return rt;
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Child positioned from its parent's top-left corner.</summary>
        private static RectTransform Child(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Place(rt, x, y, w, h);
            return rt;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Full-width strip <paramref name="h"/> tall, <paramref name="y"/> below the parent's top edge.</summary>
        private static void HStretch(RectTransform rt, float y, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -y);
            rt.sizeDelta = new Vector2(0, h);
        }

        private static void Stretch(RectTransform rt) => Inset(rt, 0);

        private static void Inset(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
