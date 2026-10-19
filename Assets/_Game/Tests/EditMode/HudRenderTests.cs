using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.EditorTools;
using Puttshack.Protocol;
using Puttshack.Runtime.Hud;
using Puttshack.Session;
using UnityEditor;
using UnityEngine;

namespace Puttshack.Tests
{
    /// <summary>Renders the baked HUD.prefab through real session states and checks which screen each one shows.</summary>
    public sealed class HudRenderTests
    {
        private static readonly Vec3d LakeCenter = new Vec3d(-30, 0, 135);

        private GameObject _hud;
        private HudView _view;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudBaker.PrefabPath);
            Assert.IsNotNull(prefab, "Bake the HUD first: Puttshack > HUD > Bake HUD.");
            _hud = Object.Instantiate(prefab);
            _view = _hud.GetComponent<HudView>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_hud);

        [Test]
        public void RoundFlow_ShowsOneScreenPerState()
        {
            var h = new SessionHarness();
            Render(h);
            AssertOnly(_view.Attract);

            h.Expect(CommandType.Start);
            h.Expect(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb", "Maya" });
            Render(h);
            AssertOnly(_view.Lobby);
            StringAssert.Contains("2 OF", _view.LobbyCount.text);
            Assert.IsTrue(_view.LobbySlots[1].Fill.enabled);
            Assert.IsTrue(_view.LobbySlots[2].Outline.enabled);

            h.Expect(CommandType.SettingsConfirm, c => c.Assistance = AssistanceProfileId.Club);
            h.Expect(CommandType.HoleReady);
            Render(h);
            AssertOnly(_view.Briefing);
            Assert.AreEqual(h.Snap.Hole.Number.ToString(), _view.BriefHoleNumber.text);
            StringAssert.Contains("CALEB", _view.BriefPlayer.Name.text);

            h.Expect(CommandType.BriefingReady);
            Render(h, "←→ aim   ↑↓ club   ENTER ready");
            AssertOnly(null);
            Assert.IsTrue(_view.Rail.activeSelf);
            Assert.IsTrue(_view.PlayHints.Root.gameObject.activeSelf);
            Assert.AreEqual("CALEB", _view.RailPlayer.Name.text);
            Assert.AreEqual($"HOLE {h.Snap.Hole.Number}", _view.RailHole.text);

            h.AimAt(h.Snap.Hole.CupM);
            double approach = h.FindPower(10, r => r.Outcome == ShotOutcome.Rest && r.FinalLie == SurfaceId.Green, 0.005, 0.4);
            Assume.That(!double.IsNaN(approach), "No approach power reaches the green.");
            h.Arm();
            Render(h);
            Assert.IsTrue(_view.Rail.activeSelf);
            Assert.IsTrue(_view.ReadyPulse.enabled, "The ready dot pulses while armed.");

            ArmState arm = h.Snap.Data.Arm;
            h.Clock.AdvanceTo(arm.EpochMs + approach * arm.SweepSeconds * 1000.0);
            h.Emulator.TriggerLane(10);
            h.Pump();
            h.Expect(CommandType.PresentationBegin);
            h.Expect(CommandType.PresentationEnd);
            Render(h);
            AssertOnly(_view.Result);
            Assert.AreEqual("CALEB", _view.ResultTab.Name.text);
            Assert.IsTrue(_view.ResultStats[2].Root.activeSelf);
            Assert.IsFalse(_view.ResultPenalty.Root.activeSelf);
            Assert.IsTrue(_view.ResultRoster[1].Root.activeSelf);
            Assert.IsFalse(_view.ResultRoster[2].Root.activeSelf);

            h.Expect(CommandType.ResultContinue);
            Render(h);
            AssertOnly(_view.Handoff);
            Assert.AreEqual("MAYA", _view.HandoffName.text);

            h.Expect(CommandType.NextPlayerReady);
            h.Expect(CommandType.Pause);
            Render(h);
            AssertOnly(_view.Paused);
            StringAssert.Contains("MAYA", _view.PausedDetail.text);

            h.Expect(CommandType.Resume);
            h.Expect(CommandType.EndRound);
            Render(h);
            AssertOnly(_view.RoundComplete);
            Assert.IsTrue(_view.RoundRows[1].Root.activeSelf);
            Assert.IsFalse(_view.RoundRows[2].Root.activeSelf);
            Assert.AreEqual("ended", _view.RoundTwist.text, "Nobody finished a hole, so nobody leads.");
            Assert.IsFalse(_view.RoundWinner.Root.activeSelf);

            Render(h, null, true);
            AssertOnly(null);
            Assert.IsTrue(_view.Scorecard.activeSelf);
        }

        [Test]
        public void WaterShot_ShowsPenaltyCard()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.AimAt(LakeCenter);
            h.Club("iron5");
            double power = h.FindPower(10, r => r.Outcome == ShotOutcome.Water, 0.01, 0.3);
            Assume.That(!double.IsNaN(power), "No power found that reaches the lake.");
            h.Shoot(10, power);
            h.Expect(CommandType.PresentationBegin);
            h.Expect(CommandType.PresentationEnd);
            Render(h);
            AssertOnly(_view.Result);
            Assert.AreEqual("WATER", _view.ResultTitle.text);
            Assert.IsTrue(_view.ResultPenalty.Root.activeSelf);
            Assert.IsFalse(_view.ResultStats[2].Root.activeSelf);
            StringAssert.Contains("NEXT: STROKE 3", _view.ResultInput.text);
        }

        [Test]
        public void UnderParHole_ShowsCelebration()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            HoleDefinition hole = h.Snap.Hole;
            h.AimAt(hole.CupM);
            double approach = double.NaN, best = double.MaxValue;
            for (double p = 0.3; p <= 1.0; p += 0.005)
            {
                ShotResult r = h.Predict(10, p);
                double d = Vec3d.HorizontalDistance(r.FinalPosition, hole.CupM);
                if (r.Outcome != ShotOutcome.Rest || r.FinalLie != SurfaceId.Green || d >= best) continue;
                best = d;
                approach = p;
            }
            Assume.That(!double.IsNaN(approach), "No approach power reaches the green.");
            h.Shoot(10, approach);
            h.FinishShot();

            h.Club(ClubCatalog.PutterId);
            h.AimAt(hole.CupM);
            double feet = Units.MetersToFeet(Vec3d.HorizontalDistance(h.Snap.CurrentProgress.Ball.Position, hole.CupM));
            PuttScale scale = feet <= 12 ? PuttScale.Short : feet <= 34 ? PuttScale.Medium : PuttScale.Long;
            h.Expect(CommandType.SetPuttScale, c => c.PuttScale = scale);
            int lane = 0;
            double putt = double.NaN;
            for (int l = 7; l <= 14 && double.IsNaN(putt); l++)
            {
                putt = h.FindPower(l, r => r.Outcome == ShotOutcome.Holed, 0.002, 0.02);
                lane = l;
            }
            Assume.That(!double.IsNaN(putt) && hole.Par > 2, "No holing putt from the approach spot.");
            h.Shoot(lane, putt);
            h.Expect(CommandType.PresentationBegin);
            h.Expect(CommandType.PresentationEnd);
            Render(h);
            AssertOnly(_view.Celebrate);
            Assert.AreEqual(hole.Par == 3 ? "birdie" : "eagle", _view.CelebrateWord.text);
            Assert.IsFalse(_view.CelebrateAce.activeSelf);
            Assert.AreEqual(hole.Par == 3, _view.CelebratePinkRings.activeSelf);
        }

        private void Render(SessionHarness h, string hints = null, bool scorecard = false) =>
            _view.Render(new HudContext
            {
                Snapshot = h.Snap,
                NowMs = h.Clock.NowMs,
                Hints = hints,
                ShowScorecard = scorecard,
                LobbyAssistance = AssistanceProfileId.Club,
                LobbyPaceCap = true,
                HomeInProShop = true,
            });

        private void AssertOnly(HudCard expected)
        {
            HudCard[] cards =
            {
                _view.Attract, _view.Lobby, _view.Briefing, _view.Result, _view.Celebrate, _view.Handoff,
                _view.HoleComplete, _view.RoundComplete, _view.Paused, _view.Fault,
            };
            foreach (HudCard c in cards)
                Assert.AreEqual(c == expected, c.Root.activeSelf, $"{c.Root.name} visibility");
            if (expected != null) Assert.IsFalse(_view.Rail.activeSelf, "The rail hides behind cards.");
        }
    }
}
