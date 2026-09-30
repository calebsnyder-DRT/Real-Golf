using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;

namespace Puttshack.Tests
{
    public sealed class SolverTests
    {
        /// <summary>Flat, calm fairway range with the cup far away and generous bounds.</summary>
        private static HoleDefinition FlatRange(params TreeObstacle[] trees)
        {
            var hole = new HoleDefinition
            {
                Id = "test-range",
                Version = "test",
                Name = "Range",
                Number = 1,
                Par = 4,
                YardageYd = 400,
                DefaultSurface = SurfaceId.Fairway,
                TeeM = new Vec3d(0, 0, 0),
                CupM = new Vec3d(0, 0, 900),
                WindMps = Vec3d.Zero,
                PlayableBoundary = Shapes.Rect(-400, -50, 400, 1000),
            };
            hole.Trees.AddRange(trees);
            return hole;
        }

        private static ShotIntent Intent(string club, double power, double yawRad = 0) => new ShotIntent
        {
            ClubId = club,
            ShotType = ClubCatalog.Get(club).ShotType,
            PuttScale = PuttScale.Medium,
            OriginM = Vec3d.Zero,
            AimForward = Vec3d.Forward,
            Lie = SurfaceId.Fairway,
            WindMps = Vec3d.Zero,
            Power01 = power,
            YawRad = yawRad,
        };

        [Test]
        public void Carry_IncreasesWithPower_ForEveryClub()
        {
            var world = new AnalyticWorld(FlatRange());
            foreach (ClubDefinition club in ClubCatalog.All)
            {
                if (club.ShotType == ShotType.Putt) continue;
                double previous = 0;
                foreach (double p in new[] { 0.25, 0.5, 0.75, 1.0 })
                {
                    ShotResult r = ShotSolver.Solve(Intent(club.Id, p), world);
                    Assert.AreEqual(ShotOutcome.Rest, r.Outcome, $"{club.Id} @ {p}");
                    Assert.Greater(r.CarryM, previous, $"{club.Id} carry must increase at power {p}");
                    previous = r.CarryM;
                }
            }
        }

        [Test]
        public void FullPower_Carry_FallsInsideClubRange()
        {
            var world = new AnalyticWorld(FlatRange());
            foreach (ClubDefinition club in ClubCatalog.All)
            {
                if (club.ShotType == ShotType.Putt) continue;
                double carryYd = Units.MetersToYards(ShotSolver.Solve(Intent(club.Id, 1.0), world).CarryM);
                Assert.That(carryYd, Is.InRange(club.RangeMinYd * 0.95, club.RangeMaxYd * 1.05), club.Id);
            }
        }

        private static HoleDefinition Carpet(SurfaceId surface)
        {
            HoleDefinition hole = FlatRange();
            hole.DefaultSurface = surface;
            return hole;
        }

        private static double RollYd(string club, double power, SurfaceId surface) =>
            Units.MetersToYards(ShotSolver.Solve(Intent(club, power), new AnalyticWorld(Carpet(surface))).RollM);

        [TestCase("driver", 15, 30)]
        [TestCase("wood3", 8, 15)]
        [TestCase("iron5", 8, 15)]
        [TestCase("iron7", 3, 8)]
        [TestCase("iron9", 3, 8)]
        [TestCase("pw", 0, 4)]
        [TestCase("sw", 0, 4)]
        [TestCase("lw", 0, 4)]
        public void FairwayRollout_IsRealistic(string club, double minYd, double maxYd)
        {
            foreach (double p in new[] { 0.75, 0.9, 1.0 })
                Assert.That(RollYd(club, p, SurfaceId.Fairway), Is.InRange(minYd, maxYd), $"{club} @ {p}");
        }

        [Test]
        public void Rough_StopsQuickly_BunkerAlmostImmediately()
        {
            foreach (ClubDefinition club in ClubCatalog.All)
            {
                if (club.ShotType == ShotType.Putt) continue;
                foreach (double p in new[] { 0.75, 1.0 })
                {
                    double fairway = RollYd(club.Id, p, SurfaceId.Fairway);
                    double light = RollYd(club.Id, p, SurfaceId.LightRough);
                    Assert.LessOrEqual(light, 5, $"{club.Id} light rough @ {p}");
                    Assert.LessOrEqual(light, fairway * 0.6 + 0.5, $"{club.Id} light rough vs fairway @ {p}");
                    Assert.LessOrEqual(RollYd(club.Id, p, SurfaceId.DeepRough), 2, $"{club.Id} deep rough @ {p}");
                    Assert.LessOrEqual(RollYd(club.Id, p, SurfaceId.Bunker), 1, $"{club.Id} bunker @ {p}");
                }
            }
        }

        [Test]
        public void ApproachLandingOnGreen_ChecksUp()
        {
            foreach (string club in new[] { "iron5", "iron7", "iron9", "pw", "sw", "lw" })
            {
                foreach (double p in new[] { 0.75, 0.9, 1.0 })
                    Assert.LessOrEqual(RollYd(club, p, SurfaceId.Green), 4, $"{club} @ {p}");
            }
        }

        [Test]
        public void Green_RollsAtStimpTen()
        {
            double decel = SurfaceCatalog.Get(SurfaceId.Green).RollingResistance;
            double power = 1.83 * 1.83 / (2 * decel * ClubCatalog.PuttScaleMaxMeters(PuttScale.Medium));
            ShotResult putt = ShotSolver.Solve(Intent(ClubCatalog.PutterId, power), new AnalyticWorld(Carpet(SurfaceId.Green)));
            Assert.AreEqual(Units.FeetToMeters(10), putt.TotalM, 0.15);
        }

        [Test]
        public void Putt_DistanceIncreasesWithPower_AndStaysOnScale()
        {
            var world = new AnalyticWorld(FlatRange());
            double previous = 0;
            foreach (double p in new[] { 0.2, 0.5, 0.8, 1.0 })
            {
                ShotResult r = ShotSolver.Solve(Intent(ClubCatalog.PutterId, p), world);
                Assert.Greater(r.TotalM, previous);
                previous = r.TotalM;
            }
            Assert.LessOrEqual(previous, ClubCatalog.PuttScaleMaxMeters(PuttScale.Medium) * 1.6);
        }

        [Test]
        public void FastBall_DoesNotTunnelThroughTrunk()
        {
            var trunk = new TreeObstacle { Id = "wall-oak", Position = new Vec2d(0, 20), TrunkRadius = 0.3, TrunkHeight = 8, CanopyRadius = 3, CanopyHeight = 10 };
            var world = new AnalyticWorld(FlatRange(trunk));
            ShotResult r = ShotSolver.Solve(Intent("driver", 1.0), world);
            Assert.IsTrue(r.Events.Exists(e => e.Type == ShotEventType.TreeHit), "Expected a trunk collision event.");
            Assert.Less(r.FinalPosition.z, 20.0, "Ball passed through the trunk.");
            foreach (TrajectorySample s in r.Samples)
            {
                double dx = s.p.x - trunk.Position.x, dz = s.p.z - trunk.Position.z;
                bool insideTrunk = dx * dx + dz * dz < trunk.TrunkRadius * trunk.TrunkRadius && s.p.y < trunk.TrunkHeight;
                Assert.IsFalse(insideTrunk, $"Sample at t={s.t} is inside the trunk.");
            }
        }

        [Test]
        public void SameIntent_ProducesSameHash()
        {
            HoleDefinition hole = GrayboxCourse.Create().Holes[0];
            var world = new AnalyticWorld(hole);
            ShotIntent intent = Intent("iron5", 0.83, 0.02);
            intent.OriginM = hole.TeeM;
            intent.AimForward = (hole.CupM - hole.TeeM).Horizontal.Normalized;
            intent.WindMps = hole.WindMps;
            intent.Lie = SurfaceId.Tee;

            ShotResult a = ShotSolver.Solve(intent, world);
            ShotResult b = ShotSolver.Solve(intent, new AnalyticWorld(GrayboxCourse.Create().Holes[0]));
            Assert.IsFalse(string.IsNullOrEmpty(a.ResultHash));
            Assert.AreEqual(a.ResultHash, b.ResultHash);
            Assert.AreEqual(a.FinalPosition, b.FinalPosition);
        }

        [Test]
        public void Results_AreFinite_AcrossLanesAndPowers()
        {
            HoleDefinition hole = GrayboxCourse.Create().Holes[0];
            var world = new AnalyticWorld(hole);
            var outcomes = new HashSet<ShotOutcome>();
            foreach (string club in new[] { "driver", "iron5", "pw", "putter" })
            {
                for (int lane = 1; lane <= 20; lane += 3)
                {
                    for (double p = 0.1; p <= 1.0; p += 0.3)
                    {
                        ShotIntent intent = Intent(club, p, Units.DegToRad((lane - 10.5) / 9.5 * ClubCatalog.Get(club).FanHalfAngleDeg));
                        intent.OriginM = hole.TeeM;
                        intent.AimForward = (hole.CupM - hole.TeeM).Horizontal.Normalized;
                        intent.WindMps = hole.WindMps;
                        ShotResult r = ShotSolver.Solve(intent, world);
                        Assert.IsTrue(r.FinalPosition.IsFinite);
                        Assert.AreNotEqual(ShotOutcome.SolverFault, r.Outcome, $"{club} lane {lane} p {p}");
                        outcomes.Add(r.Outcome);
                    }
                }
            }
            Assert.IsTrue(outcomes.Contains(ShotOutcome.Rest));
        }
    }
}
