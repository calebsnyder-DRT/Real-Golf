using System;
using System.IO;
using Puttshack.Runtime.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Renders every HUD.prefab screen with its baked sample content over the design's captured course frames
    /// (ArtSource/UI/HUD/Brand/Backdrops) to 1920x1080 PNGs in Logs/HudPreview, for checking the layout against the Claude
    /// Design without entering Play mode. Batch: <c>-executeMethod Puttshack.EditorTools.HudUIPreview.BakeAndCaptureBatch</c>.
    /// While the editor is open, creating Temp/hud-bake.request bakes the HUD and captures the previews on the next editor
    /// tick (no focus needed); Temp/shop-ui.request does the same for the Pro Shop UI (Logs/ProShopUIPreview.png). The
    /// outcome is written to Temp/hud-bake.result.
    /// </summary>
    [InitializeOnLoad]
    public static class HudUIPreview
    {
        private const string Backdrops = "ArtSource/UI/HUD/Brand/Backdrops/";
        private const string RequestFile = "Temp/hud-bake.request";
        private const string ShopRequestFile = "Temp/shop-ui.request";
        private const string ResultFile = "Temp/hud-bake.result";

        private static readonly (string name, string card, string backdrop)[] Screens =
        {
            ("1b_rail", null, "bg_tee"),
            ("1c_result", "Shot Result", "bg_fairway"),
            ("1d_birdie", "Celebration", "bg_green"),
            ("1g_attract", "Attract", "bg_flyover"),
            ("1h_setup", "Lobby", "bg_flyover"),
            ("1i_briefing", "Briefing", "bg_flyover"),
            ("1j_handoff", "Handoff", "bg_tee"),
            ("1k_hole", "Hole Complete", "bg_green"),
            ("1l_final", "Final Results", "bg_flyover"),
            ("1m_paused", "Paused", "bg_tee"),
            ("1n_fault", "Fault", "bg_tee"),
            ("scorecard", "Scorecard", "bg_fairway"),
        };

        static HudUIPreview() => EditorApplication.update += PollRequest;

        private static void PollRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool hud = File.Exists(RequestFile), shop = File.Exists(ShopRequestFile);
            if (!hud && !shop) return;
            File.Delete(hud ? RequestFile : ShopRequestFile);
            try
            {
                string done;
                if (hud)
                {
                    HudBaker.Bake();
                    done = Capture(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "HudPreview"));
                }
                else
                {
                    ProShopBaker.RebuildUI();
                    done = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProShopUIPreview.png");
                    ProShopUIPreview.Capture(done);
                }
                File.WriteAllText(ResultFile, "ok " + done);
            }
            catch (Exception e)
            {
                File.WriteAllText(ResultFile, "error " + e);
                Debug.LogException(e);
            }
        }

        [MenuItem("Puttshack/HUD/Capture HUD Previews", priority = 63)]
        public static void CaptureMenu() => EditorUtility.RevealInFinder(Capture(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "HudPreview")));

        public static void BakeAndCaptureBatch()
        {
            HudBaker.Bake();
            Capture(Environment.GetEnvironmentVariable("PUTTSHACK_HUD_PREVIEW") ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs", "HudPreview"));
        }

        /// <summary>Writes one PNG per screen into <paramref name="dir"/> and returns it.</summary>
        public static string Capture(string dir)
        {
            Directory.CreateDirectory(dir);
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                var cam = new GameObject("Preview Camera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = HudBrand.Midnight;
                cam.targetTexture = rt;

                RawImage backdrop = Backdrop(scene, cam);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudBaker.PrefabPath);
                var hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var canvas = hud.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                hud.GetComponent<CanvasScaler>().enabled = false;
                canvas.scaleFactor = 1f;
                var view = hud.GetComponent<HudView>();

                foreach ((string name, string card, string bg) in Screens)
                {
                    Show(view, card);
                    var source = new Texture2D(2, 2);
                    source.LoadImage(File.ReadAllBytes(Backdrops + bg + ".png"));
                    backdrop.texture = source;
                    Canvas.ForceUpdateCanvases();
                    foreach (LayoutGroup g in hud.GetComponentsInChildren<LayoutGroup>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)g.transform);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(source);
                }
                cam.targetTexture = null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("[Puttshack] HUD previews written to " + dir);
            return dir;
        }

        /// <summary>A full-screen image behind a Screen Space - Camera UI rendered by <paramref name="cam"/>.</summary>
        internal static RawImage Backdrop(UnityEngine.SceneManagement.Scene scene, Camera cam)
        {
            var go = new GameObject("Backdrop", typeof(RectTransform));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5f;
            canvas.scaleFactor = 1f;
            var img = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(go.transform, false);
            var r = img.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return img;
        }

        /// <summary>Shows one card (or the rail when <paramref name="card"/> is null) and hides the rest.</summary>
        private static void Show(HudView view, string card)
        {
            view.Rail.SetActive(card == null);
            view.PlayHints.Root.gameObject.SetActive(card == null);
            view.Banner.SetActive(false);
            view.Scorecard.SetActive(card == "Scorecard");
            Transform cards = view.transform.Find("Cards");
            foreach (Transform c in cards) c.gameObject.SetActive(c.name == card);
        }
    }
}
