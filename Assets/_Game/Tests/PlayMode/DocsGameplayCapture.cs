using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Puttshack.Runtime;
using Puttshack.Domain;
using Puttshack.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Puttshack.Tests
{
    /// <summary>
    /// Documentation capture: plays Main.unity from Player Setup through a tee shot, a hole hand-off and the cart drive,
    /// rendering the main camera with every overlay canvas routed through it. Skipped unless PUTTSHACK_DOCS_CAPTURE
    /// names an output folder.
    /// </summary>
    public sealed class DocsGameplayCapture
    {
        private string _dir;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameHost.ForceInMemoryStorage = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureGameplay()
        {
            _dir = Environment.GetEnvironmentVariable("PUTTSHACK_DOCS_CAPTURE");
            if (string.IsNullOrEmpty(_dir)) Assert.Ignore("Set PUTTSHACK_DOCS_CAPTURE to an output folder to run.");
            Directory.CreateDirectory(_dir);

            GameHost.ForceInMemoryStorage = true;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            GameHost host = UnityEngine.Object.FindAnyObjectByType<GameHost>();
            yield return WaitFor(() => host.Initialized, 10, "host");
            yield return new WaitForSeconds(3f);

            Assert.IsTrue(host.Setup.StartRound(), "start round");
            yield return new WaitForSeconds(1.5f);
            yield return Capture("gameplay-hole-title");

            yield return WaitFor(() => host.Coordinator.State == SessionState.BRIEFING, 10, "briefing");
            yield return new WaitForSeconds(3.5f);
            yield return Capture("gameplay-briefing");
            MethodInfo teeOff = typeof(RoundFlow).GetMethod("TeeOff", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 4 && host.Coordinator.State == SessionState.BRIEFING; i++)
            {
                teeOff.Invoke(host.Flow, null);
                yield return new WaitForSeconds(1.5f);
            }
            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_SETUP, 10, "shot setup");
            yield return new WaitForSeconds(2.5f);
            yield return Capture("gameplay-address");

            host.Send(CommandType.Arm);
            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_ARMED, 10, "armed");
            yield return new WaitForSeconds(0.9f);
            yield return Capture("gameplay-armed-meter");

            yield return new WaitForSeconds(0.7f);
            host.Emulator.TriggerLane(10);
            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_PRESENTING, 10, "presenting");
            yield return new WaitForSeconds(0.6f);
            yield return Capture("gameplay-swing");
            yield return new WaitForSeconds(1.9f);
            yield return Capture("gameplay-ball-flight");

            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_RESULT, 30, "result");
            yield return new WaitForSeconds(1.2f);
            yield return Capture("gameplay-result-live");

            host.Send(CommandType.ResultContinue);
            yield return WaitFor(() => host.Coordinator.State == SessionState.HANDOFF, 10, "handoff");
            yield return new WaitForSeconds(1f);
            yield return Capture("gameplay-handoff-live");

            host.Send(CommandType.NextPlayerReady);
            yield return new WaitForSeconds(1f);
            DebugAutoPlay.FinishHole(host, new System.Random(7));
            yield return WaitFor(() => host.Coordinator.State == SessionState.HOLE_COMPLETE, 30, "hole complete");
            yield return new WaitForSeconds(1.5f);
            yield return Capture("gameplay-hole-complete");

            host.Send(CommandType.HoleContinue);
            yield return WaitFor(() => host.Coordinator.State == SessionState.HOLE_LOAD, 10, "cart drive");
            yield return new WaitForSeconds(2.5f);
            yield return Capture("gameplay-cart-drive");

            host.SkipCartDrive();
            yield return WaitFor(() => host.Coordinator.State == SessionState.BRIEFING, 15, "hole 2 briefing");
            for (int i = 0; i < 4 && host.Coordinator.State == SessionState.BRIEFING; i++)
            {
                teeOff.Invoke(host.Flow, null);
                yield return new WaitForSeconds(1.5f);
            }
            yield return WaitFor(() => host.Coordinator.State == SessionState.SHOT_SETUP, 10, "hole 2 setup");
            yield return new WaitForSeconds(2.5f);
            yield return Capture("gameplay-hole2-address");
        }

        /// <summary>Routes every overlay canvas through the main camera for one frame so the UI lands in the image.</summary>
        private IEnumerator Capture(string name)
        {
            yield return null;
            Camera cam = Camera.main;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture previous = cam.targetTexture;
            var routed = new List<(Canvas canvas, CanvasScaler scaler, bool scalerOn)>();
            foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || !c.enabled) continue;
                var scaler = c.GetComponent<CanvasScaler>();
                routed.Add((c, scaler, scaler != null && scaler.enabled));
            }
            routed.Sort((a, b) => a.canvas.sortingOrder.CompareTo(b.canvas.sortingOrder));
            cam.targetTexture = rt;
            for (int i = 0; i < routed.Count; i++)
            {
                Canvas c = routed[i].canvas;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.05f + 0.001f * (routed.Count - i);
                if (routed[i].scaler != null) routed[i].scaler.enabled = false;
                c.scaleFactor = 1920f / 1920f;
                foreach (Graphic g in c.GetComponentsInChildren<Graphic>(true)) g.SetAllDirty();
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            Debug.Log("[DocsCapture] wrote " + name + " state=" + UnityEngine.Object.FindAnyObjectByType<GameHost>().Coordinator.State);

            foreach ((Canvas c, CanvasScaler scaler, bool on) in routed)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                if (scaler != null) scaler.enabled = on;
            }
            cam.targetTexture = previous;
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
