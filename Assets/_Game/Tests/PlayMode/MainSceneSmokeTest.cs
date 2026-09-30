using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Domain;
using Puttshack.Runtime;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Puttshack.Tests
{
    /// <summary>Loads Main, plays one shot through the emulator, and checks the baked course was used.</summary>
    public sealed class MainSceneSmokeTest
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameHost.ForceInMemoryStorage = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneShot_ThroughEmulator_OnBakedCourse()
        {
            GameHost.ForceInMemoryStorage = true;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);

            GameHost host = UnityEngine.Object.FindAnyObjectByType<GameHost>();
            Assert.IsNotNull(host, "Main.unity must contain the GameHost.");
            yield return WaitFor(() => host.Initialized, 5, "host initialization");
            host.PlaybackSpeed = 25f;

            Assert.AreEqual(SessionState.LOBBY, host.Coordinator.State, "Launch skips attract and lands in the lobby.");
            Assert.IsTrue(host.ShopOpen, "Launch opens the Pro Shop.");
            host.Shop.Back();
            Accept(host.Send(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb", "Maya" }));
            Accept(host.Send(CommandType.SettingsConfirm));
            yield return WaitFor(() => host.Coordinator.State == SessionState.BRIEFING, 5, "briefing");

            Assert.IsNotNull(host.HoleVisual, "Hole visuals were not found.");
            Assert.IsFalse(host.UsingFallbackVisuals, "Main.unity should contain the baked course, not a runtime-generated one.");

            Accept(host.Send(CommandType.BriefingReady));
            Assert.AreEqual(SessionState.SHOT_SETUP, host.Coordinator.State);
            EmulatorPanel panel = host.GetComponent<EmulatorPanel>();
            Assert.IsTrue(panel == null || !panel.Visible, "The emulator dev panel must be hidden until F1.");

            string capture = Environment.GetEnvironmentVariable("PUTTSHACK_HUD_CAPTURE");
            if (!string.IsNullOrEmpty(capture))
            {
                yield return new WaitForSeconds(1.5f);
                yield return CaptureWithHud(capture);
            }

            Accept(host.Send(CommandType.Arm));
            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_ARMED, 5, "armed");

            yield return new WaitForSeconds(0.6f);
            host.Emulator.TriggerLane(11);
            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_RESULT, 30, "shot result");

            SessionSnapshot snap = host.Coordinator.Snapshot;
            Assert.AreEqual(1, snap.Shots.Count);
            Assert.AreEqual(11, snap.LastShot.Intent.Lane);
            Assert.AreEqual(1, snap.ProgressFor(0).Played);
            Assert.IsTrue(snap.LastShot.Result.FinalPosition.IsFinite);

            Accept(host.Send(CommandType.ResultContinue));
            Assert.AreEqual(SessionState.HANDOFF, host.Coordinator.State);
            Assert.AreEqual("Maya", host.Coordinator.Snapshot.CurrentPlayer.Name);
        }

        /// <summary>Overlay canvases never reach a camera target, so the HUD is routed through the camera for one frame.</summary>
        private static IEnumerator CaptureWithHud(string path)
        {
            Camera cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            Canvas hud = GameObject.Find("HUD").GetComponent<Canvas>();
            var rt = new RenderTexture(1920, 1080, 24);
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            hud.renderMode = RenderMode.ScreenSpaceCamera;
            hud.worldCamera = cam;
            hud.planeDistance = cam.nearClipPlane + 0.05f;
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());

            hud.renderMode = RenderMode.ScreenSpaceOverlay;
            cam.targetTexture = previousTarget;
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
