using Puttshack.Runtime.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Builds HUD.prefab: the in-round HUD and overlay cards on the 1920x1080 reference grid, drawn with the HUD sprites
    /// (ArtSource/UI/HUD/make_hud_ui.py), the Pro Shop paper and club icons, and the Pro Shop's static TMP fonts.
    /// Rebuilt by <see cref="HudBaker"/>; once baked it is an ordinary prefab, so spacing, fonts and colours can be
    /// adjusted in the editor (re-baking overwrites those edits).
    /// </summary>
    public static partial class HudUIBuilder
    {
        public static GameObject Build()
        {
            LoadArt();
            var root = new GameObject(HudBaker.ObjectName, typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var view = root.AddComponent<HudView>();
            AssignArt(view);
            Transform t = root.transform;

            BuildPlayTop(t, view);
            BuildPlayBottom(t, view);
            view.PlayHints = HintRow(t, "Hints", 32f, _bold, 21f, Cream, true);
            Anchor(view.PlayHints.Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 142), view.PlayHints.Root.sizeDelta);
            view.SetHints(view.PlayHints, "\u2190\u2192 aim   \u2191\u2193 club   ENTER ready");
            BuildBanner(t, view);

            RectTransform cards = Layer(t, "Cards");
            BuildAttract(cards, view);
            BuildLobby(cards, view);
            BuildBriefing(cards, view);
            BuildResult(cards, view);
            BuildHandoff(cards, view);
            BuildHoleComplete(cards, view);
            BuildRoundComplete(cards, view);
            BuildPaused(cards, view);
            BuildFault(cards, view);
            BuildScorecard(t, view);

            foreach (HudCard c in new[] { view.Attract, view.Briefing, view.Result, view.Handoff, view.HoleComplete, view.RoundComplete, view.Paused, view.Fault })
                c.Root.SetActive(false);
            view.Scorecard.SetActive(false);
            view.Banner.SetActive(false);
            return root;
        }
    }
}
