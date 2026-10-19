using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Session;

namespace Puttshack.Tests
{
    /// <summary>The 18-hole routing over the authored layouts, and a whole round played through the real service.</summary>
    public sealed class CourseRoutingTests
    {
        [Test]
        public void FullRound_Validates_NumberedOneToEighteen()
        {
            CourseDefinition round = CourseRouting.CreateRound(GrayboxCourse.Create());
            CollectionAssert.IsEmpty(CourseValidator.Validate(round));
            Assert.AreEqual(CourseRouting.FullRound, round.Holes.Count);
            for (int i = 0; i < round.Holes.Count; i++) Assert.AreEqual(i + 1, round.Holes[i].Number);
        }

        [Test]
        public void AuthoredNumbers_PlayTheirOwnHoleUnchanged()
        {
            CourseDefinition authored = GrayboxCourse.Create();
            CourseDefinition round = CourseRouting.CreateRound(authored);
            foreach (HoleDefinition hole in authored.Holes)
            {
                HoleDefinition played = round.Holes[hole.Number - 1];
                Assert.AreEqual(hole.Id, played.Id);
                Assert.AreEqual(hole.ContentHash, played.ContentHash, "Authored bakes must keep matching.");
                Assert.IsFalse(CourseRouting.IsStandIn(played));
            }
        }

        [Test]
        public void StandIns_KeepTheirLayoutsCollision_AndPointAtItsVisuals()
        {
            CourseDefinition authored = GrayboxCourse.Create();
            CourseDefinition round = CourseRouting.CreateRound(authored);
            foreach (HoleDefinition hole in round.Holes)
            {
                if (!CourseRouting.IsStandIn(hole)) continue;
                HoleDefinition layout = authored.Holes.Find(h => h.Id == hole.LayoutId);
                Assert.IsNotNull(layout, hole.Id);
                Assert.AreEqual(layout.Id, hole.VisualId);
                HoleDefinition probe = Storage.JournalSerializer.Clone(hole);
                probe.Id = layout.Id;
                probe.Number = layout.Number;
                Assert.AreEqual(layout.ContentHash, ContentHasher.HashHole(probe), $"Hole {hole.Number} differs from {layout.Id} beyond its id and number.");
            }
        }

        [Test]
        public void ConsecutiveHoles_AreLinkedByTheCartPath()
        {
            CourseDefinition round = CourseRouting.CreateRound(GrayboxCourse.Create());
            for (int i = 1; i < round.Holes.Count; i++)
            {
                HoleDefinition from = round.Holes[i - 1], to = round.Holes[i];
                List<Vec2d> route = CourseLayout.TransitionRoute(from, to);
                double length = CourseLayout.Length(route);
                Assert.Greater(length, 20, $"{from.Number} -> {to.Number}");
                Assert.Less(length, 320, $"{from.Number} -> {to.Number}");
            }
        }

        [Test]
        public void ShortRound_TakesTheFirstNumbers()
        {
            CourseDefinition nine = CourseRouting.CreateRound(GrayboxCourse.Create(), 9);
            Assert.AreEqual(9, nine.Holes.Count);
            Assert.AreNotEqual(CourseRouting.CreateRound(GrayboxCourse.Create()).ContentHash, nine.ContentHash);
        }

        [Test]
        public void EighteenHoles_TwoPlayers_PlayThroughToFinalStandings()
        {
            CourseDefinition round = CourseRouting.CreateRound(GrayboxCourse.Create());
            var h = new SessionHarness(course: round);
            h.StartRound("Caleb", "Maya");
            for (int hole = 0; hole < round.Holes.Count; hole++)
            {
                Assert.AreEqual(hole, h.Snap.Data.HoleIndex);
                // Short taps until the stroke cap ends the hole for both players.
                for (int i = 0; i < 40 && h.Service.State == SessionState.SHOT_SETUP; i++)
                {
                    h.Shoot(10, 0.06);
                    h.FinishShot();
                }
                Assert.AreEqual(SessionState.HOLE_COMPLETE, h.Service.State, $"hole {hole + 1}");
                h.Expect(CommandType.HoleContinue);
                if (hole == round.Holes.Count - 1) break;
                h.Expect(CommandType.HoleReady);
                h.Expect(CommandType.BriefingReady);
            }

            Assert.AreEqual(SessionState.ROUND_COMPLETE, h.Service.State);
            Assert.AreEqual(RoundCompletion.Complete, h.Snap.Data.Completion);
            foreach (var row in h.Snap.Scorecard)
            {
                Assert.AreEqual(18, row.Thru, row.Name);
                Assert.IsFalse(row.Out.IsPartial);
                Assert.IsFalse(row.In.IsPartial);
                Assert.AreEqual(row.Out.Strokes + row.In.Strokes, row.CompletedTotal);
            }
        }
    }
}
