using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime;
using Puttshack.Runtime.Shop;
using Puttshack.Session;
using Puttshack.Shop;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Puttshack.Tests
{
    /// <summary>
    /// Opens the baked Pro Shop in Main from the lobby and from pause, and checks Save/Back against the session.
    /// With PUTTSHACK_PROSHOP_CAPTURE set to a folder it also writes the 1920x1080 review shots.
    /// </summary>
    public sealed class ProShopFlowTest
    {
        private static readonly List<string> Roster = new List<string> { "Caleb", "Maya", "Jordan", "Sam", "Nia", "Leo" };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameHost.ForceInMemoryStorage = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Shop_FromLobbyAndPause_SaveKeepsBackDiscards()
        {
            GameHost.ForceInMemoryStorage = true;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            GameHost host = UnityEngine.Object.FindAnyObjectByType<GameHost>();
            Assert.IsNotNull(host, "Main.unity must contain the GameHost.");
            yield return WaitFor(() => host.Initialized, 5, "host initialization");
            Assert.IsNotNull(host.Shop, "Main.unity must contain the baked Pro Shop.");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<ProShopController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Main.unity must contain exactly one Pro Shop.");

            ProShopController shop = host.Shop;
            Assert.AreEqual(SessionState.LOBBY, host.Coordinator.State, "Launch skips attract and lands in the lobby.");
            Assert.IsTrue(shop.IsOpen && shop.View.Visible, "Launch opens the Pro Shop.");
            Assert.AreEqual(ShopOrigin.Lobby, shop.Editor.Origin);
            shop.Back();
            Accept(host.Send(CommandType.RosterSet, c => c.Names = new List<string>(Roster)));
            Assert.AreEqual(SessionState.LOBBY, host.Coordinator.State);
            UnityEngine.Rendering.AmbientMode courseAmbient = RenderSettings.ambientMode;
            Assert.IsTrue(host.OpenShop(), "The shop opens from the lobby.");
            Assert.IsTrue(shop.IsOpen && shop.View.Visible);
            Assert.AreEqual(ShopOrigin.Lobby, shop.Editor.Origin);
            Assert.AreEqual(Roster.Count, shop.Editor.PlayerCount);
            yield return null;

            string capture = Environment.GetEnvironmentVariable("PUTTSHACK_PROSHOP_CAPTURE");
            if (!string.IsNullOrEmpty(capture)) yield return CaptureSet(shop, capture);

            // Back throws away try-ons for every player.
            shop.SelectPlayer(0);
            shop.SetCategory(ShopCategory.Tops);
            Assert.IsTrue(shop.TryOn("top.polo.coral"));
            shop.SelectPlayer(1);
            shop.SetCategory(ShopCategory.Bags);
            Assert.IsTrue(shop.TryOn("bag.cart.harbor"));
            Loadout calebBefore = LookOf(host, 0);
            shop.Back();
            Assert.IsFalse(shop.IsOpen);
            Assert.AreEqual(courseAmbient, RenderSettings.ambientMode, "Closing the shop restores the course lighting.");
            Assert.IsTrue(LookOf(host, 0).SameLook(calebBefore), "Back must not keep Caleb's try-on.");
            Assert.AreNotEqual("bag.cart.harbor", LookOf(host, 1).Bag, "Back must not keep Maya's try-on.");
            yield return null;

            // Save commits the edited player's look through the session and marks bought items owned.
            Assert.IsTrue(host.OpenShop());
            shop.SetCategory(ShopCategory.Bottoms);
            Assert.IsTrue(shop.TryOn("bottom.shorts.navy"));
            AssertParts(shop.Golfer, on: new[] { "Bottom_Shorts", "Head_Caleb" }, off: new[] { "Bottom_Chinos" });
            shop.SetCategory(ShopCategory.Socks);
            Assert.IsTrue(shop.TryOn("socks.crew.navy"));
            AssertParts(shop.Golfer, on: new[] { "Socks_Crew" }, off: new[] { "Socks_Ankle" });
            Assert.IsTrue(shop.Save());
            Loadout saved = LookOf(host, 0);
            Assert.AreEqual("bottom.shorts.navy", saved.Bottom);
            Assert.IsTrue(saved.Owns("bottom.shorts.navy"));
            Assert.IsFalse(shop.Editor.AnyUnsaved);
            shop.Back();
            Assert.AreEqual("bottom.shorts.navy", LookOf(host, 0).Bottom, "Back after Save keeps the saved look.");
            yield return null;

            // Into the round, pause, and edit from the pause overlay.
            Accept(host.Send(CommandType.SettingsConfirm));
            yield return WaitFor(() => host.Coordinator.State == SessionState.BRIEFING, 5, "briefing");
            Accept(host.Send(CommandType.BriefingReady));
            Assert.IsFalse(host.OpenShop(), "The shop is closed during play.");

            // The shooter addresses the ball in the look saved in the shop.
            CourseGolfer onCourse = UnityEngine.Object.FindAnyObjectByType<CourseGolfer>(FindObjectsInactive.Include);
            Assert.IsNotNull(onCourse, "Main.unity must contain the baked course golfer.");
            yield return WaitFor(() => onCourse.gameObject.activeInHierarchy, 5, "the golfer at the ball");
            int shooter = host.Coordinator.Snapshot.Data.Players.FindIndex(p => p.Id == host.Coordinator.Snapshot.CurrentPlayer.Id);
            Loadout shooterLook = LookOf(host, shooter);
            StringAssert.Contains(shooterLook.Bottom, onCourse.Appearance.AppliedKey, "The course golfer wears the saved loadout.");
            Assert.AreEqual(shooterLook.Bag, onCourse.Bag.ShownItem, "The saved bag stands beside the golfer.");
            if (!string.IsNullOrEmpty(capture))
            {
                yield return new WaitForSeconds(2f);
                yield return CaptureCamera(Path.Combine(capture, "g_course_tee.png"));
            }
            Accept(host.Send(CommandType.Pause));
            Assert.AreEqual(SessionState.PAUSED, host.Coordinator.State);
            int turn = host.Coordinator.Snapshot.Data.Players.FindIndex(p => p.Id == host.Coordinator.Snapshot.CurrentPlayer.Id);
            Assert.IsTrue(host.OpenShop());
            Assert.AreEqual(ShopOrigin.Paused, shop.Editor.Origin);
            Assert.AreEqual(turn, shop.Editor.TurnIndex);
            Assert.AreEqual(turn, shop.Editor.EditedIndex, "Paused shop starts on the player whose turn it is.");

            shop.SelectPlayer(2);
            shop.SetCategory(ShopCategory.Socks);
            Assert.IsTrue(shop.TryOn("socks.crew.cyan"));
            Assert.IsTrue(shop.Save());
            Assert.AreEqual("socks.crew.cyan", LookOf(host, 2).Socks);
            Assert.AreEqual(turn, host.Coordinator.Snapshot.Data.Players.FindIndex(p => p.Id == host.Coordinator.Snapshot.CurrentPlayer.Id),
                "Editing another player never moves the turn.");

            // Leaving the pause state closes the shop and hands the camera back.
            Accept(host.Send(CommandType.Resume));
            yield return null;
            yield return null;
            Assert.IsFalse(shop.IsOpen, "Resuming closes the shop.");
            Assert.IsFalse(shop.View.Visible);

            // Restart from pause goes home: a new session in the lobby, shop open, same players in their saved looks.
            int oldSession = host.Coordinator.Snapshot.Data.SessionId;
            Accept(host.Send(CommandType.Pause));
            Assert.IsTrue(host.Restart());
            yield return null;
            Assert.AreNotEqual(oldSession, host.Coordinator.Snapshot.Data.SessionId);
            Assert.AreEqual(SessionState.LOBBY, host.Coordinator.State);
            Assert.IsTrue(shop.IsOpen, "Restart reopens the Pro Shop.");
            Assert.AreEqual(Roster.Count, host.Coordinator.Snapshot.Data.Players.Count);
            Assert.AreEqual("bottom.shorts.navy", LookOf(host, 0).Bottom, "Restart keeps Caleb's saved look.");
            Assert.AreEqual("socks.crew.cyan", LookOf(host, 2).Socks, "Restart keeps looks saved from the pause shop.");
        }

        private static Loadout LookOf(GameHost host, int index) => CosmeticCatalog.LoadoutOf(host.Coordinator.Snapshot.Data.Players[index]);

        private static void AssertParts(GolferAppearance golfer, string[] on, string[] off)
        {
            var active = new List<string>();
            foreach (Renderer r in golfer.Parts)
            {
                if (r.gameObject.activeInHierarchy) active.Add(r.name);
            }
            string shown = string.Join(", ", active);
            foreach (string part in on) Assert.Contains(part, active, $"{part} should be worn (shown: {shown}).");
            foreach (string part in off) Assert.IsFalse(active.Contains(part), $"{part} should be hidden (shown: {shown}).");
        }

        // ---------------------------------------------------------------- Review captures

        private static IEnumerator CaptureSet(ProShopController shop, string folder)
        {
            ProShopCameraRig rig = shop.CameraRig;
            rig.HoldOverview(true);
            yield return new WaitForSeconds(0.5f);
            yield return Capture(shop, Path.Combine(folder, "a_overview.png"), withUi: false);
            rig.HoldOverview(false);
            yield return new WaitForSeconds(2.4f);

            shop.SetCategory(ShopCategory.Tops);
            shop.Focus("top.polo.royal");
            yield return Settle(shop);
            yield return Capture(shop, Path.Combine(folder, "b_tops_polo.png"));
            shop.SetCategory(ShopCategory.Socks);
            shop.TryOn("socks.crew.navy");
            shop.SetCategory(ShopCategory.Bottoms);
            shop.TryOn("bottom.shorts.khaki");
            yield return Settle(shop);
            yield return Capture(shop, Path.Combine(folder, "c_bottoms_shorts_socks.png"));

            shop.SetCategory(ShopCategory.Bags);
            shop.TryOn("bag.stand.sunset");
            yield return Settle(shop);
            yield return Capture(shop, Path.Combine(folder, "d_bags_try_on.png"));

            shop.SelectPlayer(1);
            shop.SetCategory(ShopCategory.Tops);
            shop.TryOn("top.polo.sky");
            yield return Settle(shop);
            yield return Capture(shop, Path.Combine(folder, "e_player_switched.png"));

            shop.SetCategory(ShopCategory.Clubs);
            shop.Focus("iron5");
            yield return Settle(shop);
            yield return Capture(shop, Path.Combine(folder, "f_clubs.png"));
            shop.SelectPlayer(0);
        }

        private static IEnumerator Settle(ProShopController shop)
        {
            for (int i = 0; i < 6; i++) yield return null;
            yield return WaitFor(() => shop.Studio == null || !shop.Studio.Busy, 5, "player portraits");
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>Routes the overlay canvas through the camera for one frame so the UI lands in the image.</summary>
        private static IEnumerator Capture(ProShopController shop, string path, bool withUi = true)
        {
            Camera cam = Camera.main;
            Canvas canvas = shop.View.Canvas;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            canvas.enabled = withUi;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.05f;
            // The scaler sizes glyphs from the (headless) screen; the target already is the 1920x1080 reference size.
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            canvas.scaleFactor = 1f;
            foreach (UnityEngine.UI.Graphic g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.SetAllDirty();
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = true;
            if (scaler != null) scaler.enabled = true;
            cam.targetTexture = previousTarget;
            UnityEngine.Object.Destroy(tex);
            rt.Release();
        }

        private static IEnumerator CaptureCamera(string path)
        {
            yield return null;
            Camera cam = Camera.main;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = previousTarget;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
        }

        private static void Accept(CommandReply reply) => Assert.IsTrue(reply != null && reply.Accepted, reply?.ToString());

        private static IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds, string what)
        {
            float end = UnityEngine.Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (UnityEngine.Time.realtimeSinceStartup > end) Assert.Fail($"Timed out waiting for {what}.");
                yield return null;
            }
        }
    }
}
