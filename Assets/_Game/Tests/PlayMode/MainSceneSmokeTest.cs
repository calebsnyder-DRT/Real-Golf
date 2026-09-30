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

            Accept(host.Send(CommandType.Start));
            Accept(host.Send(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb", "Maya" }));
            Accept(host.Send(CommandType.SettingsConfirm));
            yield return WaitFor(() => host.Coordinator.State == SessionState.BRIEFING, 5, "briefing");

            Assert.IsNotNull(host.HoleVisual, "Hole visuals were not found.");
            Assert.IsFalse(host.UsingFallbackVisuals, "Main.unity should contain the baked course, not a runtime-generated one.");

            Accept(host.Send(CommandType.BriefingReady));
            Assert.AreEqual(SessionState.SHOT_SETUP, host.Coordinator.State);
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
