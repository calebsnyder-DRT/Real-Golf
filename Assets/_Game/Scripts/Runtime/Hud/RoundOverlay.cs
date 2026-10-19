using System;
using System.Collections;
using System.Collections.Generic;
using Puttshack.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.Runtime.Hud
{
    /// <summary>
    /// Presentation layer above the HUD for the round's transitions: the Midnight dip between holes, the hole title that
    /// opens each flyover, and the winner banner with confetti. Built at runtime on the HUD's 1920x1080 grid and styled
    /// from the baked HUD (its fonts and rounded panel sprite), so it needs no re-bake. Runs on unscaled time.
    /// </summary>
    public sealed class RoundOverlay : MonoBehaviour
    {
        private const float TitleIn = 0.55f, TitleOut = 0.4f, TitleRise = 56f;
        private const int ConfettiCount = 110;

        private TMP_FontAsset _wide, _condensedBold, _twist, _book;
        private Sprite _panel;

        private CanvasGroup _fade;
        private Coroutine _dip;
        private Coroutine _fadeTween;

        private CanvasGroup _title;
        private RectTransform _titlePanel;
        private TMP_Text _titleEyebrow, _titleNumber, _titleName, _titlePar;
        private readonly List<Image> _titleTicks = new List<Image>();
        private float _titleShownAt = -1f, _titleHiddenAt = -1f;

        private CanvasGroup _winner;
        private TMP_Text _winnerEyebrow, _winnerName, _winnerTwist, _winnerDetail, _winnerHint;
        private float _winnerShownAt = -1f;

        private RectTransform _confettiRoot;
        private readonly List<Confetto> _confetti = new List<Confetto>();
        private bool _confettiOn;

        private struct Confetto
        {
            public RectTransform Rect;
            public Vector2 Velocity;
            public float Spin, Sway, Phase;
        }

        public bool ReducedMotion { get; set; }
        public bool Dipping => _dip != null;
        public bool TitleVisible => _titleShownAt >= 0f;
        public bool WinnerVisible => _winnerShownAt >= 0f;

        public static RoundOverlay Create(HudView hud)
        {
            var go = new GameObject("Round Overlay", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD (10) so the dip covers it, below the Pro Shop UI (20).
            canvas.sortingOrder = 15;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var overlay = go.AddComponent<RoundOverlay>();
            overlay.Borrow(hud);
            overlay.Build();
            return overlay;
        }

        /// <summary>Fonts and the rounded panel sprite from the baked HUD; the fallback HUD has neither.</summary>
        private void Borrow(HudView hud)
        {
            TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
            _wide = hud != null && hud.BriefHoleNumber != null ? hud.BriefHoleNumber.font : fallback;
            _condensedBold = hud != null && hud.BriefPar != null ? hud.BriefPar.font : fallback;
            _twist = hud != null && hud.RoundTwist != null ? hud.RoundTwist.font : fallback;
            _book = hud != null && hud.BriefHazard != null ? hud.BriefHazard.font : fallback;
            Image panel = hud != null && hud.HoleColumns.Length > 0 && hud.HoleColumns[0].Root != null ? hud.HoleColumns[0].Root.GetComponent<Image>() : null;
            _panel = panel != null ? panel.sprite : null;
        }

        // ---------------------------------------------------------------- Dip

        /// <summary>Fades to Midnight, runs <paramref name="atBlack"/>, holds a moment for cameras to settle, then fades back.</summary>
        public void Dip(Action atBlack, float outSeconds = 0.4f, float holdSeconds = 0.18f, float inSeconds = 0.6f)
        {
            if (_dip != null) return;
            _dip = StartCoroutine(DipRoutine(atBlack, outSeconds, holdSeconds, inSeconds));
        }

        /// <summary>Starts from Midnight and fades in (a scene that appears without a transition, e.g. the first tee).</summary>
        public void Reveal(float seconds = 0.9f)
        {
            if (_dip != null) return;
            _fade.alpha = 1f;
            FadeTo(0f, seconds);
        }

        private IEnumerator DipRoutine(Action atBlack, float outSeconds, float holdSeconds, float inSeconds)
        {
            if (ReducedMotion)
            {
                outSeconds *= 0.5f;
                inSeconds *= 0.5f;
            }
            yield return FadeRoutine(1f, outSeconds);
            try
            {
                atBlack?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            float end = UnityEngine.Time.unscaledTime + holdSeconds;
            // At least two frames so a camera snapped on the next frame is already in place when the fade lifts.
            yield return null;
            yield return null;
            while (UnityEngine.Time.unscaledTime < end) yield return null;
            yield return FadeRoutine(0f, inSeconds);
            _dip = null;
        }

        private void FadeTo(float alpha, float seconds)
        {
            if (_fadeTween != null) StopCoroutine(_fadeTween);
            _fadeTween = StartCoroutine(FadeRoutine(alpha, seconds));
        }

        private IEnumerator FadeRoutine(float alpha, float seconds)
        {
            float from = _fade.alpha;
            _fade.blocksRaycasts = true;
            for (float t = 0f; t < seconds; t += UnityEngine.Time.unscaledDeltaTime)
            {
                _fade.alpha = Mathf.Lerp(from, alpha, Smooth(t / seconds));
                yield return null;
            }
            _fade.alpha = alpha;
            _fade.blocksRaycasts = alpha > 0.01f;
            _fadeTween = null;
        }

        // ---------------------------------------------------------------- Hole title

        public void ShowTitle(HoleDefinition hole, int holeIndex, int holeCount)
        {
            _titleEyebrow.text = $"HOLE {holeIndex + 1} OF {holeCount}";
            _titleNumber.text = hole.Number.ToString();
            _titleName.text = (hole.Name ?? string.Empty).ToUpperInvariant();
            _titlePar.text = $"PAR {hole.Par}<space=24>·<space=24>{hole.YardageYd} YD";
            for (int i = 0; i < _titleTicks.Count; i++)
            {
                Image tick = _titleTicks[i];
                bool present = i < holeCount;
                tick.gameObject.SetActive(present);
                if (!present) continue;
                tick.color = i < holeIndex ? HudBrand.White : i == holeIndex ? HudBrand.Sun : HudBrand.White.WithAlpha(0.18f);
            }
            LayoutTicks(holeCount);
            _titleShownAt = UnityEngine.Time.unscaledTime;
            _titleHiddenAt = -1f;
            _title.gameObject.SetActive(true);
        }

        public void HideTitle()
        {
            if (_titleShownAt < 0f) return;
            _titleShownAt = -1f;
            _titleHiddenAt = UnityEngine.Time.unscaledTime;
        }

        // ---------------------------------------------------------------- Winner

        public void ShowWinner(string eyebrow, string name, string twist, string detail, Color identity, string hint)
        {
            _winnerEyebrow.text = eyebrow;
            _winnerName.text = name;
            _winnerTwist.text = twist;
            _winnerTwist.color = HudBrand.Sun;
            _winnerDetail.text = detail;
            _winnerHint.text = hint;
            _winnerName.color = HudBrand.White;
            _winnerEyebrow.color = Color.Lerp(identity, HudBrand.White, 0.35f);
            _winnerShownAt = UnityEngine.Time.unscaledTime;
            _winner.gameObject.SetActive(true);
            SetConfetti(true);
        }

        public void HideWinner()
        {
            _winnerShownAt = -1f;
            _winner.gameObject.SetActive(false);
        }

        public void SetConfetti(bool on)
        {
            if (on == _confettiOn) return;
            _confettiOn = on;
            _confettiRoot.gameObject.SetActive(on && !ReducedMotion);
            if (on) for (int i = 0; i < _confetti.Count; i++) Respawn(i, true);
        }

        // ---------------------------------------------------------------- Animation

        private void Update()
        {
            float now = UnityEngine.Time.unscaledTime;
            AnimateTitle(now);
            AnimateWinner(now);
            if (_confettiOn && !ReducedMotion) AnimateConfetti(UnityEngine.Time.unscaledDeltaTime);
        }

        private void AnimateTitle(float now)
        {
            if (!_title.gameObject.activeSelf) return;
            float alpha, rise;
            if (_titleShownAt >= 0f)
            {
                float p = ReducedMotion ? 1f : Mathf.Clamp01((now - _titleShownAt) / TitleIn);
                float e = EaseOut(p);
                alpha = e;
                rise = -TitleRise * (1f - e);
            }
            else
            {
                float p = ReducedMotion ? 1f : Mathf.Clamp01((now - _titleHiddenAt) / TitleOut);
                alpha = 1f - Smooth(p);
                rise = TitleRise * 0.5f * Smooth(p);
                if (p >= 1f) _title.gameObject.SetActive(false);
            }
            _title.alpha = alpha;
            _titlePanel.anchoredPosition = new Vector2(0f, 40f + rise);
        }

        private void AnimateWinner(float now)
        {
            if (_winnerShownAt < 0f) return;
            float t = now - _winnerShownAt;
            float p = ReducedMotion ? 1f : Mathf.Clamp01(t / 0.7f);
            _winner.alpha = EaseOut(p);
            float pop = ReducedMotion ? 1f : p < 0.6f ? Mathf.Lerp(0.6f, 1.06f, EaseOut(p / 0.6f)) : Mathf.Lerp(1.06f, 1f, (p - 0.6f) / 0.4f);
            _winnerName.rectTransform.localScale = new Vector3(pop, pop, 1f);
            float twist = ReducedMotion ? 1f : Mathf.Clamp01((t - 0.35f) / 0.5f);
            _winnerTwist.alpha = EaseOut(twist);
            _winnerHint.alpha = ReducedMotion ? 1f : Mathf.Clamp01((t - 1.2f) / 0.5f);
        }

        private void AnimateConfetti(float dt)
        {
            for (int i = 0; i < _confetti.Count; i++)
            {
                Confetto c = _confetti[i];
                c.Phase += dt;
                Vector2 p = c.Rect.anchoredPosition + c.Velocity * dt;
                p.x += Mathf.Sin(c.Phase * 2.3f) * c.Sway * dt;
                c.Rect.anchoredPosition = p;
                c.Rect.localRotation = Quaternion.Euler(Mathf.Sin(c.Phase * 3.1f) * 70f, 0f, c.Phase * c.Spin);
                _confetti[i] = c;
                if (p.y < -1140f) Respawn(i, false);
            }
        }

        private void Respawn(int i, bool burst)
        {
            Confetto c = _confetti[i];
            float x = UnityEngine.Random.Range(-1000f, 1000f);
            float y = burst ? UnityEngine.Random.Range(-900f, 100f) : UnityEngine.Random.Range(20f, 120f);
            c.Rect.anchoredPosition = new Vector2(x, y);
            c.Velocity = new Vector2(UnityEngine.Random.Range(-40f, 40f), -UnityEngine.Random.Range(170f, 330f));
            c.Spin = UnityEngine.Random.Range(-260f, 260f);
            c.Sway = UnityEngine.Random.Range(40f, 140f);
            c.Phase = UnityEngine.Random.Range(0f, 6f);
            _confetti[i] = c;
        }

        // ---------------------------------------------------------------- Build

        private void Build()
        {
            var root = (RectTransform)transform;

            _confettiRoot = Layer(root, "Confetti");
            _confettiRoot.anchorMin = _confettiRoot.anchorMax = new Vector2(0.5f, 1f);
            _confettiRoot.sizeDelta = Vector2.zero;
            Color[] colors = { HudBrand.Pink, HudBrand.Aqua, HudBrand.Sun, HudBrand.White, HudBrand.PinkHot, HudBrand.Plum };
            for (int i = 0; i < ConfettiCount; i++)
            {
                Image piece = Solid(_confettiRoot, $"Piece {i}", colors[i % colors.Length]);
                piece.rectTransform.anchorMin = piece.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                piece.rectTransform.sizeDelta = new Vector2(UnityEngine.Random.Range(10f, 18f), UnityEngine.Random.Range(18f, 30f));
                _confetti.Add(new Confetto { Rect = piece.rectTransform });
            }
            _confettiRoot.gameObject.SetActive(false);

            BuildTitle(root);
            BuildWinner(root);

            Image fade = Solid(root, "Fade", HudBrand.Midnight);
            Stretch(fade.rectTransform);
            _fade = fade.gameObject.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
            _fade.interactable = false;
        }

        /// <summary>The hole card that opens each flyover, in the briefing card's language: pink hole block, Midnight panel.</summary>
        private void BuildTitle(RectTransform root)
        {
            RectTransform layer = Layer(root, "Hole Title");
            Stretch(layer);
            _title = layer.gameObject.AddComponent<CanvasGroup>();
            _title.blocksRaycasts = false;

            const float w = 1240, h = 330, block = 330;
            Image panel = Panel(layer, "Card", HudBrand.Midnight.WithAlpha(0.92f));
            _titlePanel = panel.rectTransform;
            _titlePanel.anchorMin = _titlePanel.anchorMax = new Vector2(0.5f, 0.5f);
            _titlePanel.sizeDelta = new Vector2(w, h);
            panel.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            Image pink = Solid(_titlePanel, "Hole Block", HudBrand.Pink);
            Place(pink.rectTransform, 0, 0, block, h);
            Label(_titlePanel, "Hole Label", _condensedBold, 28, HudBrand.Midnight, "HOLE", 48, 46, block - 60, 34, TextAlignmentOptions.TopLeft, 0.14f);
            _titleNumber = Label(_titlePanel, "Number", _wide, 200, HudBrand.Midnight, "7", 40, 74, block - 60, 210, TextAlignmentOptions.TopLeft);

            const float x = block + 60, tw = w - block - 110;
            _titleEyebrow = Label(_titlePanel, "Eyebrow", _condensedBold, 28, HudBrand.Muted, "HOLE 7 OF 18", x, 52, tw, 34, TextAlignmentOptions.TopLeft, 0.14f);
            _titleName = Label(_titlePanel, "Name", _wide, 84, HudBrand.White, "STILLWATER CLIFFS", x, 96, tw, 100, TextAlignmentOptions.TopLeft);
            _titleName.enableAutoSizing = true;
            _titleName.fontSizeMin = 44;
            _titleName.fontSizeMax = 84;
            _titlePar = Label(_titlePanel, "Par", _condensedBold, 40, HudBrand.Sun, "PAR 3  ·  165 YD", x, 204, tw, 48, TextAlignmentOptions.TopLeft, 0.08f);

            for (int i = 0; i < CourseRouting.FullRound; i++)
            {
                Image tick = Panel(_titlePanel, $"Tick {i + 1}", HudBrand.White);
                _titleTicks.Add(tick);
            }
            LayoutTicks(CourseRouting.FullRound);
            layer.gameObject.SetActive(false);
        }

        /// <summary>One tick per hole of the round along the bottom of the title card; the nines are split by a wider gap.</summary>
        private void LayoutTicks(int holes)
        {
            const float x0 = 330 + 60, y = 278, size = 18, gap = 10, nineGap = 26;
            float x = x0;
            for (int i = 0; i < _titleTicks.Count; i++)
            {
                if (i == 9) x += nineGap - gap;
                Place(_titleTicks[i].rectTransform, x, y, size, size);
                x += size + gap;
            }
        }

        private void BuildWinner(RectTransform root)
        {
            RectTransform layer = Layer(root, "Winner");
            Stretch(layer);
            _winner = layer.gameObject.AddComponent<CanvasGroup>();
            _winner.blocksRaycasts = false;

            Image shade = Solid(layer, "Shade", HudBrand.Midnight.WithAlpha(0.45f));
            shade.rectTransform.anchorMin = new Vector2(0, 1);
            shade.rectTransform.anchorMax = new Vector2(1, 1);
            shade.rectTransform.pivot = new Vector2(0.5f, 1);
            shade.rectTransform.sizeDelta = new Vector2(0, 420);
            shade.rectTransform.anchoredPosition = Vector2.zero;

            _winnerEyebrow = Label(layer, "Eyebrow", _condensedBold, 32, HudBrand.White, "FINAL  ·  18 HOLES", 0, 70, 1920, 40, TextAlignmentOptions.Top, 0.2f);
            _winnerName = Label(layer, "Name", _wide, 150, HudBrand.White, "CALEB", 0, 112, 1920, 170, TextAlignmentOptions.Top);
            _winnerName.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _winnerName.rectTransform.anchoredPosition = new Vector2(960, -(112 + 85));
            _winnerName.enableAutoSizing = true;
            _winnerName.fontSizeMin = 80;
            _winnerName.fontSizeMax = 150;
            _winnerTwist = Label(layer, "Twist", _twist, 150, HudBrand.Sun, "wins", 0, 236, 1920, 170, TextAlignmentOptions.Top);
            _winnerDetail = Label(layer, "Detail", _condensedBold, 36, HudBrand.White, "62  ·  −3", 0, 360, 1920, 44, TextAlignmentOptions.Top, 0.12f);

            _winnerHint = Label(layer, "Hint", _condensedBold, 30, HudBrand.White, "ENTER  STANDINGS", 0, 0, 1920, 40, TextAlignmentOptions.Bottom, 0.14f);
            _winnerHint.rectTransform.anchorMin = new Vector2(0, 0);
            _winnerHint.rectTransform.anchorMax = new Vector2(0, 0);
            _winnerHint.rectTransform.pivot = new Vector2(0, 0);
            _winnerHint.rectTransform.anchoredPosition = new Vector2(0, 64);
            layer.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- Widgets

        private static RectTransform Layer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image Solid(Transform parent, string name, Color color)
        {
            RectTransform rt = Layer(parent, name);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image Panel(Transform parent, string name, Color color)
        {
            Image image = Solid(parent, name, color);
            if (_panel != null)
            {
                image.sprite = _panel;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        /// <summary>Text placed from the parent's top-left on the design grid (y down), like the HUD builder's TextAt.</summary>
        private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, Color color, string text,
            float x, float y, float w, float h, TextAlignmentOptions align, float spacingEm = 0f)
        {
            RectTransform rt = Layer(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.alignment = align;
            t.characterSpacing = spacingEm * 100f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            Place(rt, x, y, w, h);
            return t;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static float Smooth(float p)
        {
            p = Mathf.Clamp01(p);
            return p * p * (3f - 2f * p);
        }

        /// <summary>The HUD design's ease-out (see <see cref="HudMotion"/>).</summary>
        private static float EaseOut(float p) => 1f - Mathf.Pow(1f - Mathf.Clamp01(p), 3.2f);
    }
}
