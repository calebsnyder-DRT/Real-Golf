using System;
using System.IO;
using System.Text;
using Puttshack.Runtime.Shop;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.EditorTools
{
    /// <summary>
    /// Renders ProShopUI.prefab on its own (flat backdrop, no shop) to a 1920x1080 PNG and logs every label's mesh, for
    /// checking typography without entering Play mode. Batch: <c>-executeMethod Puttshack.EditorTools.ProShopUIPreview.CaptureBatch</c>
    /// with PUTTSHACK_UI_PREVIEW set to the output file.
    /// </summary>
    public static class ProShopUIPreview
    {
        [MenuItem("Puttshack/Pro Shop/Capture UI Preview", priority = 45)]
        public static void CaptureMenu()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProShopUIPreview.png");
            Capture(path);
            EditorUtility.RevealInFinder(path);
        }

        public static void CaptureBatch() =>
            Capture(Environment.GetEnvironmentVariable("PUTTSHACK_UI_PREVIEW") ?? "/tmp/proshop_ui_preview.png");

        public static void Capture(string path)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewPreviewScene();
            var cam = new GameObject("Preview Camera").AddComponent<Camera>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.27f, 0.2f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProShopUIBuilder.PrefabPath);
            var ui = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var canvas = ui.GetComponent<Canvas>();
            canvas.enabled = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var scaler = ui.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            canvas.scaleFactor = 1f;
            Fill(ui.GetComponent<ProShopView>());

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            var log = new StringBuilder("[Puttshack] Pro Shop UI labels:\n");
            foreach (TMP_Text t in ui.GetComponentsInChildren<TMP_Text>(true))
            {
                t.ForceMeshUpdate(true, true);
                log.AppendLine($"  {t.name} '{t.text}' chars={t.textInfo.characterCount} verts={t.mesh?.vertexCount ?? -1} font={t.font?.name} mat={t.fontSharedMaterial?.name} active={t.isActiveAndEnabled}");
            }
            Debug.Log(log.ToString());
            Canvas.ForceUpdateCanvases();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            EditorSceneManager.ClosePreviewScene(scene);
            Debug.Log("[Puttshack] Pro Shop UI preview written to " + path);
        }

        private static void Fill(ProShopView view)
        {
            view.Show(true);
            string[] cats = { "TOPS", "BOTTOMS", "SHOES", "SOCKS", "BAGS", "CLUBS" };
            for (int i = 0; i < view.Categories.Length && i < cats.Length; i++) view.SetCategory(i, cats[i], null, i == 4, i == 4);
            string[] names = { "PUTTSHACK CLASSIC", "MIDNIGHT STAND", "SUNSET STAND", "LAGOON STAND", "CLUBHOUSE WHITE" };
            for (int i = 0; i < view.Rows.Length; i++)
            {
                view.SetRow(i, new ShopRowModel
                {
                    Name = names[i % names.Length],
                    Detail = i == 2 ? "STAND BAG \u00b7 EQUIPPED" : "STAND BAG \u00b7 350 CR",
                    Focused = i == 2,
                    ZoneActive = true,
                    Equipped = i == 2,
                    ForSale = i != 0 && i != 2,
                });
            }
            view.SetScroll(false, true, "OWNED  <color=#00CBE7>3/9</color>");
            view.ShowLoadoutDetails("EQUIPPED");
            string[] slots = { "POLO", "SHORTS", "SOCKS", "SHOES", "BELT", "STAND BAG" };
            string[] values = { "ROYAL BLUE", "KHAKI", "CREW NAVY", "WHITE SPIKELESS", "BLACK LEATHER", "SUNSET STAND" };
            for (int i = 0; i < view.Summary.Length; i++) view.SetSummary(i, null, slots[i], values[i], i == 5);
            view.SetNote("BAGS ARE COSMETIC \u00b7 SAME 9 CLUBS");
            view.SetButton(view.Save, "SAVE LOADOUT \u00b7 350 CR", true, false, true);
            view.SetButton(view.Reset, "RESET", true, false, false);
            view.SetBack("BACK TO LOBBY", false);
            string[] roster = { "Caleb", "Maya", "Jordan", "Sam", "Nia", "Leo" };
            view.LayoutPlayers(roster.Length);
            for (int i = 0; i < roster.Length; i++)
                view.SetPlayer(i, new ShopPlayerModel { Name = roster[i], Identity = Puttshack.Runtime.Palette.Player(i), Edited = i == 0, Focused = i == 0, Turn = false });
            view.SetPlayerHints(true);
            view.SetHints("<color=#00CBE7>\u2191\u2193</color> BROWSE   <color=#00CBE7>ENTER</color> TRY ON\n<color=#00CBE7>\u2192</color> SAVE   <color=#00CBE7>A / D</color> ROTATE\n<color=#00CBE7>Q / E</color> PLAYER   <color=#00CBE7>ESC</color> BACK");
        }
    }
}
