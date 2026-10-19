using UnityEngine;

namespace Puttshack.Runtime.Hud
{
    public enum HudMotionKind
    {
        /// <summary>Slides up 48 px while fading in (riseIn).</summary>
        Rise,
        /// <summary>Scales 0.6 → 1.06 → 1 with a spring while fading in (popIn).</summary>
        Pop,
        /// <summary>Looping ring that grows from 0.55 to 1.25 and fades out (ringOut).</summary>
        Ring,
        /// <summary>Looping opacity breathe between 1 and 0.55 (readyPulse).</summary>
        Pulse,
        /// <summary>Looping scale/opacity throb around the element's base opacity (haloPulse).</summary>
        Halo,
        /// <summary>Looping rise of 140 px that fades in and out (dotDrift).</summary>
        Drift,
    }

    /// <summary>
    /// Plays one of the HUD design's CSS keyframe animations on the element, restarting whenever it is enabled (each time
    /// its card is shown). Runs on unscaled time; under <see cref="Reduced"/> every element sits at its resting pose.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class HudMotion : MonoBehaviour
    {
        public static bool Reduced;

        public HudMotionKind Kind;
        public float Delay;
        public float Duration = 0.45f;
        /// <summary>Opacity at rest; the halo multiplies its throb by it.</summary>
        public float BaseAlpha = 1f;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _basePos;
        private bool _captured;
        private float _start;

        private bool Loops => Kind == HudMotionKind.Ring || Kind == HudMotionKind.Pulse || Kind == HudMotionKind.Halo || Kind == HudMotionKind.Drift;

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
            if (!_captured)
            {
                _basePos = _rect.anchoredPosition;
                _captured = true;
            }
            _start = UnityEngine.Time.unscaledTime;
            Apply(0f);
        }

        private void OnDisable()
        {
            if (_rect == null) return;
            if (Moves) _rect.anchoredPosition = _basePos;
            _rect.localScale = Vector3.one;
            _group.alpha = BaseAlpha;
        }

        private void Update() => Apply(UnityEngine.Time.unscaledTime - _start);

        private void Apply(float elapsed)
        {
            if (Reduced)
            {
                Pose(Kind == HudMotionKind.Ring || Kind == HudMotionKind.Drift ? 0f : BaseAlpha, 1f, 0f);
                return;
            }
            float t = elapsed - Delay;
            if (t < 0f)
            {
                bool hidden = Kind != HudMotionKind.Pulse && Kind != HudMotionKind.Halo;
                Pose(hidden ? 0f : BaseAlpha, Kind == HudMotionKind.Pop ? 0.6f : 1f, Kind == HudMotionKind.Rise ? -48f : 0f);
                return;
            }
            float d = Mathf.Max(0.01f, Duration);
            float p = Loops ? Mathf.Repeat(t, d) / d : Mathf.Clamp01(t / d);
            switch (Kind)
            {
                case HudMotionKind.Rise:
                {
                    float e = EaseOut(p);
                    Pose(e * BaseAlpha, 1f, -48f * (1f - e));
                    break;
                }
                case HudMotionKind.Pop:
                {
                    float s = p < 0.6f ? Mathf.Lerp(0.6f, 1.06f, Spring(p / 0.6f)) : Mathf.Lerp(1.06f, 1f, (p - 0.6f) / 0.4f);
                    Pose(Mathf.Clamp01(p / 0.6f) * BaseAlpha, s, 0f);
                    break;
                }
                case HudMotionKind.Ring:
                {
                    float a = p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f;
                    Pose(a * BaseAlpha, Mathf.Lerp(0.55f, 1.25f, EaseOut(p)), 0f);
                    break;
                }
                case HudMotionKind.Pulse:
                {
                    float w = 0.5f - 0.5f * Mathf.Cos(p * Mathf.PI * 2f);
                    Pose(Mathf.Lerp(1f, 0.55f, w) * BaseAlpha, 1f, 0f);
                    break;
                }
                case HudMotionKind.Halo:
                {
                    float w = 0.5f - 0.5f * Mathf.Cos(p * Mathf.PI * 2f);
                    Pose(Mathf.Lerp(0.9f, 0.35f, w) * BaseAlpha, Mathf.Lerp(1f, 1.12f, w), 0f);
                    break;
                }
                case HudMotionKind.Drift:
                {
                    float a = p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.8f;
                    Pose(a * BaseAlpha, 1f, 140f * EaseOut(p));
                    break;
                }
            }
        }

        /// <summary>Only Rise and Drift move the element, so the others are safe inside layout groups.</summary>
        private bool Moves => Kind == HudMotionKind.Rise || Kind == HudMotionKind.Drift;

        private void Pose(float alpha, float scale, float rise)
        {
            _group.alpha = alpha;
            _rect.localScale = new Vector3(scale, scale, 1f);
            if (Moves) _rect.anchoredPosition = _basePos + new Vector2(0f, rise);
        }

        /// <summary>Approximates the design's ease-out, cubic-bezier(.2,.7,.1,1).</summary>
        private static float EaseOut(float p) => 1f - Mathf.Pow(1f - p, 3.2f);

        /// <summary>Approximates the design's spring, cubic-bezier(.34,1.56,.64,1), over the first 60% of popIn.</summary>
        private static float Spring(float p) => 1f - Mathf.Pow(1f - p, 2.4f);
    }
}
