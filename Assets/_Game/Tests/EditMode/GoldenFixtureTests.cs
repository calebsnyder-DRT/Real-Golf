using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Rules;

namespace Puttshack.Tests
{
    /// <summary>Golden fixtures from spec section 43.</summary>
    public sealed class GoldenFixtureTests
    {
        private const double Eps = 1e-9;

        [Test]
        public void Meter_TriangleWave_MatchesFixtures()
        {
            Assert.AreEqual(0.4, MeterWaveform.Power(800, 2.0), Eps);
            Assert.AreEqual(0.6, MeterWaveform.Power(2800, 2.0), Eps);
            Assert.AreEqual(0.0, MeterWaveform.Power(0, 2.0), Eps);
            Assert.AreEqual(1.0, MeterWaveform.Power(2000, 2.0), Eps);
            Assert.AreEqual(0.0, MeterWaveform.Power(4000, 2.0), Eps);
        }

        [Test]
        public void Lanes_MapIntoTheFan()
        {
            double half = Units.DegToRad(12);
            Assert.AreEqual(-12.0, Units.RadToDeg(LaneMapper.RawYawRad(1, half)), 1e-9);
            Assert.AreEqual(12.0, Units.RadToDeg(LaneMapper.RawYawRad(20, half)), 1e-9);
            Assert.AreEqual(5.6842, Units.RadToDeg(LaneMapper.RawYawRad(15, half)), 1e-4);
            Assert.IsFalse(LaneMapper.IsValidLane(0));
            Assert.IsFalse(LaneMapper.IsValidLane(21));
        }

        [Test]
        public void Units_ConvertExactly()
        {
            Assert.AreEqual(164.592, Units.YardsToMeters(180), 1e-9);
            Assert.AreEqual(3.048, Units.FeetToMeters(10), 1e-9);
        }

        [Test]
        public void Assistance_DeadbandAndGain()
        {
            AssistanceProfile club = AssistanceProfiles.Get(AssistanceProfileId.Club);
            Assert.AreEqual(0, Assistance.ApplyYaw(Units.DegToRad(0.5), 0, club), Eps);
            Assert.AreEqual(Units.DegToRad((5.0 - 0.6) * 0.85), Assistance.ApplyYaw(Units.DegToRad(5.0), 0, club), 1e-12);
            AssistanceProfile pro = AssistanceProfiles.Get(AssistanceProfileId.Pro);
            Assert.AreEqual(Units.DegToRad(-3), Assistance.ApplyYaw(Units.DegToRad(-3), 0, pro), 1e-12);
        }

        [Test]
        public void Water_OnFirstShot_ScoresTwo_NextLabelStrokeThree()
        {
            var progress = new HoleProgress { Status = HoleStatus.ACTIVE, Played = 1, Penalties = 1 };
            Assert.AreEqual(2, progress.StrokesSoFar);
            Assert.AreEqual(3, ScoreEngine.NextStrokeNumber(progress));
        }

        [Test]
        public void LiveScorecard_ExcludesInProgressHole()
        {
            var pars = new List<int> { 4, 4, 4, 4, 4, 4, 3 };
            var progress = new List<HoleProgress>();
            foreach (int s in new[] { 3, 4, 3, 4, 4, 4 }) progress.Add(Holed(s));
            progress.Add(new HoleProgress { Status = HoleStatus.ACTIVE, Played = 2 });

            ScorecardRow row = ScoreEngine.BuildRow(1, "Caleb", pars, progress);
            Assert.AreEqual(22, row.CompletedTotal);
            Assert.AreEqual(6, row.Thru);
            Assert.AreEqual("2*", row.Cells[6].Display);
            Assert.AreEqual("-2", row.ToParDisplay);
        }

        [Test]
        public void Par24_ToPar_Fixture()
        {
            var pars = new List<int> { 4, 4, 4, 4, 4, 4 };
            var expected = new (string name, int[] scores, int total, string toPar)[]
            {
                ("Caleb", new[] { 3, 4, 3, 4, 4, 4 }, 22, "-2"),
                ("Maya", new[] { 4, 5, 4, 4, 4, 4 }, 25, "+1"),
                ("Jordan", new[] { 3, 3, 4, 3, 4, 4 }, 21, "-3"),
                ("Sam", new[] { 4, 4, 3, 4, 4, 4 }, 23, "-1"),
                ("Nia", new[] { 5, 4, 4, 5, 4, 4 }, 26, "+2"),
                ("Leo", new[] { 4, 4, 4, 4, 4, 4 }, 24, "E"),
            };
            for (int i = 0; i < expected.Length; i++)
            {
                var progress = new List<HoleProgress>();
                foreach (int s in expected[i].scores) progress.Add(Holed(s));
                ScorecardRow row = ScoreEngine.BuildRow(i + 1, expected[i].name, pars, progress);
                Assert.AreEqual(expected[i].total, row.CompletedTotal, expected[i].name);
                Assert.AreEqual(expected[i].toPar, row.ToParDisplay, expected[i].name);
            }
        }

        [Test]
        public void PaceCap_RecordsMaxAndRawCount()
        {
            var settings = new SessionSettings();
            var progress = new HoleProgress { Status = HoleStatus.ACTIVE, Played = 7, Penalties = 1 };
            Assert.IsTrue(PaceCap.Reached(progress, 3, settings));
            PaceCap.ApplyCap(progress, 3, settings);
            Assert.AreEqual(HoleStatus.CAPPED, progress.Status);
            Assert.AreEqual(8, progress.CompletionScore);
            ScorecardRow row = ScoreEngine.BuildRow(1, "Maya", new List<int> { 3 }, new List<HoleProgress> { progress });
            Assert.AreEqual("8M", row.Cells[0].Display);
            Assert.AreEqual(8, row.CompletedTotal);
        }

        [Test]
        public void TurnScheduler_SkipsCompletedPlayers()
        {
            var statuses = new List<HoleStatus> { HoleStatus.HOLED, HoleStatus.ACTIVE, HoleStatus.CAPPED, HoleStatus.ACTIVE };
            Assert.AreEqual(1, TurnScheduler.FirstActive(statuses));
            Assert.AreEqual(3, TurnScheduler.NextActive(statuses, 1));
            Assert.AreEqual(1, TurnScheduler.NextActive(statuses, 3));
            statuses[1] = HoleStatus.HOLED;
            statuses[3] = HoleStatus.HOLED;
            Assert.AreEqual(TurnScheduler.None, TurnScheduler.NextActive(statuses, 3));
        }

        [Test]
        public void GrayboxCourse_Validates_AndHashIsStable()
        {
            CourseDefinition a = GrayboxCourse.Create();
            CourseDefinition b = GrayboxCourse.Create();
            CollectionAssert.IsEmpty(CourseValidator.Validate(a));
            Assert.AreEqual(a.ContentHash, b.ContentHash);
            Assert.AreEqual(a.Holes[0].ContentHash, b.Holes[0].ContentHash);
            Assert.AreEqual(72, Sum(GrayboxCourse.ProposedPars));
        }

        private static int Sum(IReadOnlyList<int> values)
        {
            int total = 0;
            foreach (int v in values) total += v;
            return total;
        }

        private static HoleProgress Holed(int strokes) =>
            new HoleProgress { Status = HoleStatus.HOLED, Played = strokes, CompletionScore = strokes };
    }
}
