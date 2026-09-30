using UnityEngine;
using UnityEngine.UI;

namespace RealGolf.UI
{
    /// <summary>Helpers for assembling the HUD from code.</summary>
    public static class UIFactory
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null)
                {
                    return _font;
                }

                // Unity renamed the built-in font across versions; try both before
                // falling back to whatever the OS provides.
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                if (_font == null)
                {
                    _font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica", "Arial", "SF Pro Text" }, 24);
                }

                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image CreatePanel(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <param name="font">
        /// Overrides the built-in face. The HUD leaves this null and gets the OS
        /// font; the menu passes a Kenney face from <see cref="UITheme"/>.
        /// </param>
        public static Text CreateText(
            string name,
            Transform parent,
            string content,
            int fontSize,
            TextAnchor anchor = TextAnchor.UpperLeft,
            FontStyle style = FontStyle.Normal,
            Font font = null)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font != null ? font : Font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.fontStyle = style;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            Anchor(rect, Vector2.zero, Vector2.one, new Vector2(padding, padding), new Vector2(-padding, -padding));
        }

        /// <summary>Adds a soft drop shadow so text stays readable over bright terrain.</summary>
        public static Text WithShadow(this Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }
}
