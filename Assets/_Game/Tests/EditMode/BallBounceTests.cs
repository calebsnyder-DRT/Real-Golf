using System;
using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Views;
using Puttshack.Simulation;

namespace Puttshack.Tests
{
    /// <summary>Pins the surfaces-1.2 ball response: livelier landings, slightly longer full shots, unchanged putting.</summary>
    public sealed class BallBounceTests
    {
        private static HoleDefinition Range(SurfaceId surface, params HazardVolume[] hazards)
        {
            var hole = new HoleDefinition
            {
                Id = "bounce-range",
                Version = "test",
                Name = "Range",
                Number = 1,
                Par = 4,
                YardageYd = 400,
                DefaultSurface = surface,
                TeeM = Vec3d.Zero,
                CupM = new Vec3d(0, 0, 900),
                WindMps = Vec3d.Zero,
                PlayableBoundary = Shapes.Rect(-400, -50, 400, 1000),
            };
            hole.Hazards.AddRange(hazards);
            return hole;
        }

        private static ShotIntent Intent(string club, double power, SurfaceId lie = SurfaceId.Fairway) => new ShotIntent
        {
            ClubId = club,
            ShotType = ClubCatalog.Get(club).ShotType,
            PuttScale = PuttScale.Medium,
            OriginM = Vec3d.Zero,
            AimForward = Vec3d.Forward,
            Lie = lie,
            WindMps = Vec3d.Zero,
            Power01 = power,
        };

        /// <summary>Full shots launch from fairway onto a carpet of <paramref name="surface"/>; putts start on it.</summary>
        private static ShotResult Solve(string club, double power, SurfaceId surface) =>
            ShotSolver.Solve(Intent(club, power, club == ClubCatalog.PutterId ? surface : SurfaceId.Fairway), new AnalyticWorld(Range(surface)));

        private static int Bounces(ShotResult r) => r.Events.FindAll(e => e.Type == ShotEventType.Bounce).Count;

        /// <summary>Apex height above the take-off point of each hop, in order.</summary>
        private static List<double> HopHeights(ShotResult r)
        {
            var heights = new List<double>();
            List<ShotEvent> ground = r.Events.FindAll(e =>
                e.Type == ShotEventType.FirstContact || e.Type == ShotEventType.Bounce || e.Type == ShotEventType.StartRoll);
            for (int i = 0; i < ground.Count; i++)
            {
                if (ground[i].Type != ShotEventType.Bounce) continue;
                double from = ground[i].t;
                double to = i + 1 < ground.Count ? ground[i + 1].t : r.DurationS;
                double top = 0;
                foreach (TrajectorySample s in r.Samples)
                {
                    if (s.t > from && s.t < to) top = Math.Max(top, s.p.y - ground[i].p.y);
                }
                heights.Add(top);
            }
            return heights;
        }

        private static IEnumerable<ClubDefinition> FullClubs()
        {
            foreach (ClubDefinition club in ClubCatalog.All)
            {
                if (club.ShotType != ShotType.Putt) yield return club;
            }
        }

        [Test]
        public void FairwayLanding_Rebounds_WithAVisibleSecondHop()
        {
            foreach (ClubDefinition club in FullClubs())
            {
                foreach (double p in new[] { 0.75, 0.9, 1.0 })
                {
                    ShotResult r = Solve(club.Id, p, SurfaceId.Fairway);
                    int first = r.Events.FindIndex(e => e.Type == ShotEventType.FirstContact);
                    Assert.AreEqual(ShotEventType.Bounce, r.Events[first + 1].Type, $"{club.Id} @ {p}: first landing must rebound");
                    Assert.GreaterOrEqual(Bounces(r), 2, $"{club.Id} @ {p}");
                    List<double> hops = HopHeights(r);
                    Assert.That(hops[0], Is.InRange(1.2, 3.5), $"{club.Id} @ {p} first hop (m)");
                    Assert.GreaterOrEqual(hops[1], 0.2, $"{club.Id} @ {p} second hop (m)");
                }
            }
        }

        [Test]
        public void GreenAndRough_Rebound_BunkerPlugs()
        {
            foreach (ClubDefinition club in FullClubs())
            {
                foreach (double p in new[] { 0.75, 1.0 })
                {
                    ShotResult rough = Solve(club.Id, p, SurfaceId.LightRough);
                    Assert.GreaterOrEqual(Bounces(rough), 1, $"{club.Id} @ {p} light rough");
                    Assert.GreaterOrEqual(HopHeights(rough)[0], 0.5, $"{club.Id} @ {p} light rough hop (m)");

                    ShotResult bunker = Solve(club.Id, p, SurfaceId.Bunker);
                    Assert.AreEqual(0, Bounces(bunker), $"{club.Id} @ {p} bunker must plug");
                    Assert.AreEqual(ShotOutcome.Rest, bunker.Outcome);

                    if (club.Id == "driver" || club.Id == "wood3") continue;
                    ShotResult green = Solve(club.Id, p, SurfaceId.Green);
                    Assert.GreaterOrEqual(Bounces(green), 1, $"{club.Id} @ {p} green");
                    Assert.GreaterOrEqual(HopHeights(green)[0], 0.8, $"{club.Id} @ {p} green hop (m)");
                }
            }
        }

        [Test]
        public void WaterLanding_StopsWithoutBouncing()
        {
            var lake = new HazardVolume { Id = "lake", DropPointId = "drop", Polygon = Shapes.Rect(-60, 120, 60, 220) };
            ShotResult r = ShotSolver.Solve(Intent("iron5", 1.0), new AnalyticWorld(Range(SurfaceId.Fairway, lake)));
            Assert.AreEqual(ShotOutcome.Water, r.Outcome);
            Assert.AreEqual(0, Bounces(r));
            Assert.AreEqual("lake", r.HazardId);
        }

        /// <summary>Flat, calm fairway carry and total (yd) under surfaces-1.1, before the bounce retune.</summary>
        [TestCase("driver", 0.75, 204.2, 230.6, 0.03)]
        [TestCase("driver", 1.0, 264.1, 281.7, 0.03)]
        [TestCase("wood3", 0.75, 176.2, 190.9, 0.03)]
        [TestCase("wood3", 1.0, 225.0, 235.9, 0.03)]
        [TestCase("iron5", 0.75, 148.8, 160.3, 0.03)]
        [TestCase("iron5", 1.0, 190.0, 199.5, 0.03)]
        [TestCase("iron7", 0.75, 125.3, 133.2, 0.03)]
        [TestCase("iron7", 1.0, 160.0, 167.2, 0.03)]
        [TestCase("iron9", 0.75, 103.4, 108.4, 0.03)]
        [TestCase("iron9", 1.0, 132.0, 136.9, 0.03)]
        [TestCase("pw", 0.75, 82.2, 85.4, 0.025)]
        [TestCase("pw", 1.0, 105.0, 108.3, 0.025)]
        [TestCase("sw", 0.75, 61.1, 62.6, 0.02)]
        [TestCase("sw", 1.0, 78.0, 79.6, 0.02)]
        [TestCase("lw", 0.75, 37.6, 38.2, 0.02)]
        [TestCase("lw", 1.0, 48.0, 48.6, 0.02)]
        public void FullShots_GoATadFurther_WithCarryUnchanged(string club, double power, double carryYd, double totalYd, double minGain)
        {
            ShotResult r = Solve(club, power, SurfaceId.Fairway);
            Assert.AreEqual(carryYd, Units.MetersToYards(r.CarryM), 0.1, "Carry calibration must not move.");
            double gain = Units.MetersToYards(r.TotalM) / totalYd - 1;
            Assert.That(gain, Is.InRange(minGain, 0.06), $"{club} @ {power} total gain");
        }

        /// <summary>Flat-green roll (m) per medium-scale power under surfaces-1.1. Power 0.5 is the 20 ft putt.</summary>
        [TestCase(0.2, 2.430)]
        [TestCase(0.5, 6.084)]
        [TestCase(0.8, 9.739)]
        [TestCase(1.0, 12.176)]
        public void PuttDistances_AreUnchanged(double power, double totalM)
        {
            ShotResult r = Solve(ClubCatalog.PutterId, power, SurfaceId.Green);
            Assert.AreEqual(totalM, r.TotalM, 0.005);
            Assert.AreEqual(0, Bounces(r));
        }

        [Test]
        public void TwentyFootPutt_StillRollsTwentyFeet()
        {
            double power = Units.FeetToMeters(20) / ClubCatalog.PuttScaleMaxMeters(PuttScale.Medium);
            ShotResult r = Solve(ClubCatalog.PutterId, power, SurfaceId.Green);
            Assert.AreEqual(20, r.TotalM / Units.FeetToMeters(1), 0.15);
        }

        [Test]
        public void RenderedBall_IsNearRealSize()
        {
            Assert.AreEqual(0.04267, SolverParameters.Default.BallRadius * 2, 1e-4, "The simulated ball is regulation size.");
            Assert.That(BallView.VisualScale, Is.InRange(1f, 1.5f), "Rendered ball is at most 1.5x the simulated one.");
        }

        /// <summary>Representative shots on the shipped hole; logs the numbers quoted in tuning reviews.</summary>
        [Test]
        public void CurrentHole_ShotReport()
        {
            HoleDefinition hole = GrayboxCourse.Create().Holes[0];
            var world = new AnalyticWorld(hole);
            Vec3d aim = (hole.CupM - hole.TeeM).Horizontal.Normalized;
            TestContext.WriteLine($"{hole.Id} ({SurfaceCatalog.Version}, {Versions.Simulation})");
            foreach (var (club, power) in new[] { ("driver", 1.0), ("iron5", 1.0), ("iron5", 0.9), ("pw", 1.0) })
            {
                ShotIntent intent = Intent(club, power, SurfaceId.Tee);
                intent.OriginM = hole.TeeM;
                intent.AimForward = aim;
                intent.WindMps = hole.WindMps;
                ShotResult r = ShotSolver.Solve(intent, world);
                Assert.AreNotEqual(ShotOutcome.SolverFault, r.Outcome, club);
                Assert.IsTrue(r.FinalPosition.IsFinite, club);
                TestContext.WriteLine($"{club} @{power:F2}: carry {Units.MetersToYards(r.CarryM):F1} yd, bounces {Bounces(r)}, total {Units.MetersToYards(r.TotalM):F1} yd, {r.Outcome} on {r.FinalLie}");
            }

            Vec3d from = hole.CupM - aim * Units.FeetToMeters(20);
            from.y = world.GroundHeight(from.x, from.z);
            ShotIntent putt = Intent(ClubCatalog.PutterId, 0.5, SurfaceId.Green);
            putt.OriginM = from;
            putt.AimForward = aim.RotateYaw(0.25);
            putt.WindMps = hole.WindMps;
            ShotResult rolled = ShotSolver.Solve(putt, world);
            Assert.AreNotEqual(ShotOutcome.SolverFault, rolled.Outcome);
            TestContext.WriteLine($"20 ft putt @0.50 (off line): total {rolled.TotalM / Units.FeetToMeters(1):F1} ft, {rolled.Outcome} on {rolled.FinalLie}");
        }
    }
}
