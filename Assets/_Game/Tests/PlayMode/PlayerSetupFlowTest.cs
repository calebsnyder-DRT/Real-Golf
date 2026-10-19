using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime;
using Puttshack.Runtime.Setup;
using Puttshack.Runtime.Shop;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Puttshack.Tests
{
    /// <summary>Launch lands in Player Setup; the count, names and golfer choices reach the session and the preview.</summary>
    public sealed class PlayerSetupFlowTest
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameHost.ForceInMemoryStorage = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Setup_CountNamesAndLooks_ReachTheRound()
        {
            GameHost.ForceInMemoryStorage = true;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            GameHost host = UnityEngine.Object.FindAnyObjectByType<GameHost>();
            Assert.IsNotNull(host, "Main.unity must contain the GameHost.");
            yield return WaitFor(() => host.Initialized, 5, "host initialization");
            Assert.IsNotNull(host.Setup, "Main.unity must contain the baked Player Setup.");

            PlayerSetupController setup = host.Setup;
            ProShopController shop = host.Shop;
            Assert.AreEqual(SessionState.LOBBY, host.Coordinator.State);
            Assert.IsTrue(host.SetupOpen && setup.View.Canvas.enabled, "Launch opens Player Setup.");
            Assert.IsFalse(shop.IsOpen, "The shop stays closed behind setup.");

            Assert.IsTrue(setup.SetCount(SessionCoordinator.MaxPlayers + 1));
            Assert.AreEqual(SessionCoordinator.MaxPlayers, host.Coordinator.Snapshot.Data.Players.Count, "The roster is capped.");
            Assert.IsTrue(setup.SetCount(3));
            Assert.AreEqual(3, host.Coordinator.Snapshot.Data.Players.Count);
            Assert.IsTrue(setup.Rename(2, "  Jordan "));
            Assert.AreEqual("Jordan", host.Coordinator.Snapshot.Data.Players[2].Name, "Names are trimmed.");
            Assert.IsFalse(setup.Rename(2, "   "), "A blank name is refused.");

            setup.Select(1);
            yield return Settle(shop);
            Assert.AreEqual(1, shop.Golfer.Shown, "Maya previews on her own body.");
            AssertHair(shop, ProfileCatalog.PartLong);

            Assert.IsTrue(setup.SetGender(Gender.Male));
            Assert.IsTrue(setup.SetHairStyle("style.long"));
            Assert.IsTrue(setup.SetHairColor("hair.blonde"));
            Assert.IsTrue(setup.SetSkin("skin.deep"));
            yield return Settle(shop);
            Assert.AreEqual(0, shop.Golfer.Shown, "Male uses Caleb's body.");
            AssertHair(shop, ProfileCatalog.PartLong);
            PlayerProfile saved = host.Coordinator.Snapshot.Data.Players[1].Profile;
            Assert.AreEqual("Male/skin.deep/hair.blonde/style.long", saved.ToString());

            Assert.IsTrue(setup.SetHairStyle("style.shaved"));
            yield return Settle(shop);
            AssertHair(shop, null);

            string capture = Environment.GetEnvironmentVariable("PUTTSHACK_SETUP_CAPTURE");
            if (!string.IsNullOrEmpty(capture)) yield return CaptureSet(setup, shop, capture);

            Assert.IsTrue(setup.StartRound());
            yield return WaitFor(() => host.Coordinator.State == SessionState.BRIEFING, 5, "briefing");
            Assert.IsFalse(host.SetupOpen, "Setup closes when the round starts.");
            Assert.AreEqual(3, host.Coordinator.Snapshot.Data.Players.Count, "Every set-up player gets a turn.");
        }

        private static void AssertHair(ProShopController shop, string part)
        {
            foreach (string hair in new[] { ProfileCatalog.PartShort, ProfileCatalog.PartLong })
            {
                Renderer r = shop.Golfer.ShownPart(hair);
                Assert.IsNotNull(r, $"{hair} is baked onto body {shop.Golfer.Shown}.");
                Assert.AreEqual(hair == part, r.gameObject.activeInHierarchy, $"{hair} on body {shop.Golfer.Shown}");
            }
        }

        // ---------------------------------------------------------------- Review captures

        private static IEnumerator CaptureSet(PlayerSetupController setup, ProShopController shop, string folder)
        {
            setup.Select(0);
            yield return Settle(shop);
            yield return Capture(setup, Path.Combine(folder, "a_caleb_native.png"));

            setup.Select(1);
            Assert.IsTrue(setup.SetHairStyle("style.long"));
            yield return Settle(shop);
            yield return Capture(setup, Path.Combine(folder, "b_male_long_blonde_deep.png"));

            Assert.IsTrue(setup.SetGender(Gender.Female));
            Assert.IsTrue(setup.SetHairStyle("style.short"));
            Assert.IsTrue(setup.SetHairColor("hair.copper"));
            Assert.IsTrue(setup.SetSkin("skin.porcelain"));
            yield return Settle(shop);
            yield return Capture(setup, Path.Combine(folder, "c_female_short_copper_porcelain.png"));

            Assert.IsTrue(setup.SetHairStyle("style.long"));
            Assert.IsTrue(setup.SetHairColor("hair.black"));
            Assert.IsTrue(setup.SetSkin("skin.medium"));
            yield return Settle(shop);
            yield return Capture(setup, Path.Combine(folder, "d_maya_native.png"));

            setup.Select(2);
            yield return Settle(shop);
            yield return Capture(setup, Path.Combine(folder, "e_jordan_default.png"));
        }

        private static IEnumerator Settle(ProShopController shop)
        {
            for (int i = 0; i < 6; i++) yield return null;
            yield return WaitFor(() => shop.Studio == null || !shop.Studio.Busy, 5, "player portraits");
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>Routes the overlay canvas through the camera for one frame so the UI lands in the image.</summary>
        private static IEnumerator Capture(PlayerSetupController setup, string path)
        {
            Camera cam = Camera.main;
            Canvas canvas = setup.View.Canvas;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.05f;
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
            if (scaler != null) scaler.enabled = true;
            cam.targetTexture = previousTarget;
            UnityEngine.Object.Destroy(tex);
            rt.Release();
        }

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
