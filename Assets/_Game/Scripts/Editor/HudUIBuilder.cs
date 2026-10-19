using Puttshack.Runtime.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Builds HUD.prefab from the Claude Design "Puttshack Pro HUD" (round 1) on the 1920x1080 reference grid: a single
    /// Midnight rail in the shot, full-screen overlay cards for everything else, the flat brand sprites
    /// (ArtSource/UI/HUD/make_hud_flat.py) and the Ringside fonts (<see cref="HudFonts"/>). Rebuilt by <see cref="HudBaker"/>;
    /// once baked it is an ordinary prefab, so spacing, fonts and colours can be adjusted in the editor (re-baking
    /// overwrites those edits).
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

            BuildRail(t, view);
            BuildPlayHints(t, view);
            BuildBanner(t, view);

            RectTransform cards = Layer(t, "Cards");
            BuildAttract(cards, view);
            BuildLobby(cards, view);
            BuildBriefing(cards, view);
            BuildResult(cards, view);
            BuildCelebrate(cards, view);
            BuildHandoff(cards, view);
            BuildHoleComplete(cards, view);
            BuildRoundComplete(cards, view);
            BuildPaused(cards, view);
            BuildFault(cards, view);
            BuildScorecard(t, view);
            FinishPills(root);

            foreach (HudCard c in new[] { view.Attract, view.Lobby, view.Briefing, view.Result, view.Celebrate, view.Handoff, view.HoleComplete, view.RoundComplete, view.Paused, view.Fault })
                c.Root.SetActive(false);
            view.Scorecard.SetActive(false);
            view.Banner.SetActive(false);
            return root;
        }

        private static HudCard FullCard(Transform parent, string name, out RectTransform rt)
        {
            rt = Layer(parent, name);
            return new HudCard { Root = rt.gameObject };
        }
    }
}
