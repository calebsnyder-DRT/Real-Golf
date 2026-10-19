using System;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Simulation;

namespace Puttshack.Tests
{
    /// <summary>Pins the sim-1.3 cup: a little more forgiving than sim-1.2 without holing clearly bad putts.</summary>
    public sealed class CupCaptureTests
    {
        private const double PuttLength = 3.0;
        private static readonly double Decel = SurfaceCatalog.Get(SurfaceId.Green).RollingResistance;

        /// <summary>The sim-1.2 cup: hard CaptureSpeed cut-off inside the profile's CaptureRadius, no assist.</summary>
        private static SolverParameters Legacy() => new SolverParameters
        {
            Cup = new CupCaptureParameters { EdgeMarginM = 0, CentreSpeedScale = 1, EdgeSpeedScale = 1, AssistAccel = 0, RestDropMarginM = 0 },
        };

        private static readonly AnalyticWorld FlatGreen = new AnalyticWorld(new HoleDefinition
        {
            Id = "cup-test",
            Version = "test",
            Name = "Cup",
            Number = 1,
            Par = 3,
            YardageYd = 100,
            DefaultSurface = SurfaceId.Green,
            TeeM = Vec3d.Zero,
            CupM = new Vec3d(0, 0, PuttLength),
            WindMps = Vec3d.Zero,
            PlayableBoundary = Shapes.Rect(-50, -50, 50, 50),
        });

        private static ShotResult Putt(double lateralM, double startSpeed, SolverParameters p = null)
        {
            double max = ClubCatalog.PuttScaleMaxMeters(PuttScale.Long);
            var intent = new ShotIntent
            {
                ClubId = ClubCatalog.PutterId,
                ShotType = ShotType.Putt,
                PuttScale = PuttScale.Long,
                OriginM = new Vec3d(lateralM, 0, 0),
                AimForward = Vec3d.Forward,
                Lie = SurfaceId.Green,
                WindMps = Vec3d.Zero,
                Power01 = startSpeed * startSpeed / (2 * Decel * max),
            };
            return ShotSolver.Solve(intent, FlatGreen, p);
        }

        /// <summary>Straight putt passing <paramref name="lateralM"/> from the cup centre at <paramref name="speedAtCup"/>.</summary>
        private static ShotResult Through(double lateralM, double speedAtCup, SolverParameters p = null) =>
            Putt(lateralM, Math.Sqrt(speedAtCup * speedAtCup + 2 * Decel * PuttLength), p);

        /// <summary>Straight putt that runs out of pace <paramref name="pastM"/> beyond the cup line (negative: short).</summary>
        private static ShotResult Dying(double lateralM, double pastM, SolverParameters p = null) =>
            Putt(lateralM, Math.Sqrt(2 * Decel * (PuttLength + pastM)), p);

        private static double FromCup(Vec3d basePosition) => Vec3d.HorizontalDistance(basePosition, new Vec3d(0, 0, PuttLength));

        [Test]
        public void DeadCentre_ModeratelyHighSpeed_IsHoled()
        {
            ShotResult before = Through(0, 1.7, Legacy());
            Assert.AreEqual(ShotOutcome.Rest, before.Outcome, "sim-1.2 lipped this out");
            Assert.IsTrue(before.Events.Exists(e => e.Type == ShotEventType.LipOut));

            Assert.AreEqual(ShotOutcome.Holed, Through(0, 1.7).Outcome);
            Assert.AreEqual(ShotOutcome.Holed, Through(0.02, 1.7).Outcome);
        }

        [Test]
        public void EdgeCatch_ModerateSpeed_IsHoled()
        {
            Assert.AreEqual(ShotOutcome.Rest, Through(0.079, 1.0, Legacy()).Outcome, "sim-1.2 slid past this one");
            Assert.AreEqual(ShotOutcome.Holed, Through(0.079, 1.0).Outcome);
            Assert.AreEqual(ShotOutcome.Holed, Through(0.072, 1.5).Outcome);
        }

        [Test]
        public void EdgeCatch_IsLessForgivingOfPaceThanCentre()
        {
            Assert.AreEqual(ShotOutcome.Holed, Through(0, 1.75).Outcome);
            ShotResult edge = Through(0.078, 1.75);
            Assert.AreEqual(ShotOutcome.Rest, edge.Outcome);
            Assert.IsTrue(edge.Events.Exists(e => e.Type == ShotEventType.LipOut));
        }

        [TestCase(0.08, 0.0)]
        [TestCase(0.078, 0.03)]
        [TestCase(0.0, -0.075)]
        [TestCase(0.04, -0.06)]
        public void BallDyingAtTheLip_Drops(double lateralM, double pastM)
        {
            ShotResult before = Dying(lateralM, pastM, Legacy());
            Assert.AreEqual(ShotOutcome.Rest, before.Outcome, "sim-1.2 left this hanging");
            Assert.Less(FromCup(before.FinalPosition), 0.086, "...with the ball visibly touching the cup");

            Assert.AreEqual(ShotOutcome.Holed, Dying(lateralM, pastM).Outcome);
        }

        [Test]
        public void NoBallIsLeftRestingOnTheLip()
        {
            for (double past = -0.12; past <= 0.12; past += 0.005)
            {
                for (double lateral = 0; lateral <= 0.12; lateral += 0.005)
                {
                    ShotResult r = Dying(lateral, past);
                    if (r.Outcome == ShotOutcome.Rest)
                        Assert.Greater(FromCup(r.FinalPosition), 0.087, $"lateral {lateral:F3} past {past:F3}");
                }
            }
        }

        [Test]
        public void ClearlyTooFastOrWide_StillMisses()
        {
            foreach (double lateral in new[] { 0.0, 0.03, 0.06 })
                Assert.AreNotEqual(ShotOutcome.Holed, Through(lateral, 2.1).Outcome, $"too fast at {lateral}");
            for (double speed = 0.3; speed <= 2.5; speed += 0.1)
                Assert.AreNotEqual(ShotOutcome.Holed, Through(0.105, speed).Outcome, $"wide at {speed:F1} m/s");
            Assert.AreNotEqual(ShotOutcome.Holed, Dying(0.0, -0.1).Outcome, "short of the lip");
            Assert.AreNotEqual(ShotOutcome.Holed, Dying(0.1, 0.0).Outcome, "dies beside the cup");
        }

        /// <summary>Speed x lateral-offset grid: nothing sim-1.2 holed now misses; logs the before/after holed fraction.</summary>
        [Test]
        public void PuttGrid_IsASupersetOfBefore_AndReportsHoledFraction()
        {
            int cells = 0, before = 0, after = 0;
            for (int si = 1; si <= 40; si++)
            {
                double speed = si * 0.05;
                for (int li = 0; li <= 36; li++)
                {
                    double lateral = li * 0.0025;
                    bool was = Through(lateral, speed, Legacy()).Outcome == ShotOutcome.Holed;
                    bool now = Through(lateral, speed).Outcome == ShotOutcome.Holed;
                    Assert.IsFalse(was && !now, $"regressed at {speed:F2} m/s, {lateral * 1000:F1} mm");
                    cells++;
                    if (was) before++;
                    if (now) after++;
                }
            }
            TestContext.WriteLine($"Holed, speed at cup 0.05-2.0 m/s x offset 0-90 mm: before {before}/{cells} = {100.0 * before / cells:F1}%, after {after}/{cells} = {100.0 * after / cells:F1}%");
            Assert.Greater(after, before);
        }

        [Test]
        public void MaxCaptureSpeed_FallsFromCentreToEdge()
        {
            var cup = new CupProfile();
            var c = new CupCaptureParameters();
            double radius = CupCapture.CaptureRadius(cup, c);
            Assert.AreEqual(cup.CaptureRadius + c.EdgeMarginM, radius, 1e-12);
            Assert.AreEqual(cup.CaptureSpeed * c.CentreSpeedScale, CupCapture.MaxCaptureSpeed(0, cup, c), 1e-12);
            Assert.AreEqual(cup.CaptureSpeed * c.EdgeSpeedScale, CupCapture.MaxCaptureSpeed(radius, cup, c), 1e-12);
            Assert.AreEqual(0, CupCapture.MaxCaptureSpeed(radius + 1e-6, cup, c));
            double previous = double.MaxValue;
            for (double b = 0; b <= radius; b += radius / 20)
            {
                double v = CupCapture.MaxCaptureSpeed(b, cup, c);
                Assert.LessOrEqual(v, previous);
                previous = v;
            }
            Assert.GreaterOrEqual(CupCapture.MaxCaptureSpeed(cup.CaptureRadius, cup, c), cup.CaptureSpeed,
                "Inside the old radius the cup is never stricter than sim-1.2.");
        }

        [Test]
        public void RimAssist_PullsOnlySlowBallsNearTheRim()
        {
            var cup = new CupProfile();
            var c = new CupCaptureParameters();
            Vec3d centre = new Vec3d(0, 0, 1);
            double edge = CupCapture.CaptureRadius(cup, c);
            Vec3d pull = CupCapture.RimAssist(new Vec3d(edge + 0.005, 0, 1), 0.1, centre, cup, c);
            Assert.Less(pull.x, 0, "Points at the cup.");
            Assert.AreEqual(0, pull.z, 1e-12);
            Assert.LessOrEqual(pull.Length, c.AssistAccel);
            Assert.AreEqual(Vec3d.Zero, CupCapture.RimAssist(new Vec3d(edge + c.AssistReachM + 0.001, 0, 1), 0.1, centre, cup, c));
            Assert.AreEqual(Vec3d.Zero, CupCapture.RimAssist(new Vec3d(edge + 0.005, 0, 1), c.AssistMaxSpeed, centre, cup, c));
        }

        [TestCase(0.08, 0.0)]
        [TestCase(0.079, 1.0)]
        [TestCase(0.0, 1.7)]
        public void HoledBall_VisiblyDropsIntoTheCup(double lateralM, double speedAtCup)
        {
            ShotResult r = speedAtCup > 0 ? Through(lateralM, speedAtCup) : Dying(lateralM, 0);
            Assert.AreEqual(ShotOutcome.Holed, r.Outcome);
            double depth = SolverParameters.Default.Cup.DropDepthM;
            int n = r.Samples.Count;
            TrajectorySample sunk = r.Samples[n - 2];
            TrajectorySample final = r.Samples[n - 1];
            Assert.AreEqual(0, FromCup(final.p), 1e-9, "Final position is the cup centre.");
            Assert.AreEqual(0, final.p.y, 1e-9);
            Assert.AreEqual(0, FromCup(sunk.p), 1e-9);
            Assert.AreEqual(-depth, sunk.p.y, 1e-9, "Playback ends where the ball view holds a holed ball.");
            Assert.AreEqual(final.t, sunk.t, 1e-12);

            int dropStart = r.Samples.FindLastIndex(s => s.p.y > -1e-9 && FromCup(s.p) > CupCapture.CaptureRadius(new CupProfile(), SolverParameters.Default.Cup) - 1e-6);
            Assert.Greater(sunk.t - r.Samples[dropStart].t, 0.1, "The drop takes visible time.");
            for (int i = Math.Max(1, dropStart); i < n - 1; i++)
            {
                TrajectorySample a = r.Samples[i - 1], b = r.Samples[i];
                Assert.LessOrEqual(b.p.y, a.p.y + 1e-9, $"sample {i} rises");
                double dt = b.t - a.t;
                Assert.Greater(dt, 0);
                Assert.LessOrEqual(Vec3d.HorizontalDistance(a.p, b.p) / dt, Math.Max(speedAtCup, 0.3) * 2.2 + 0.2, $"sample {i} jumps");
            }
        }

        [Test]
        public void Drops_NeedsPaceStrictlyBelowTheOffsetLimit()
        {
            var cup = new CupProfile();
            var c = SolverParameters.Default.Cup;
            Assert.IsTrue(CupCapture.Drops(0.0, cup.CaptureSpeed * 1.1, cup, c));
            Assert.IsFalse(CupCapture.Drops(CupCapture.CaptureRadius(cup, c) * 0.98, cup.CaptureSpeed * 1.1, cup, c));
            Assert.IsFalse(CupCapture.Drops(0.0, cup.CaptureSpeed * c.CentreSpeedScale, cup, c));
        }
    }
}
