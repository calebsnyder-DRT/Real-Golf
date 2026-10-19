using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Session;
using Puttshack.Storage;

namespace Puttshack.Tests
{
    public sealed class SessionFlowTests
    {
        private static readonly Vec3d LakeCenter = new Vec3d(-30, 0, 135);

        [Test]
        public void Commands_AreRevisionGuarded_AndIdempotent()
        {
            var h = new SessionHarness();
            SessionCommand start = SessionCommand.Of(CommandType.Start, h.Service.Revision);
            Assert.IsTrue(h.Service.Apply(start).Accepted);
            long revision = h.Service.Revision;

            CommandReply retry = h.Service.Apply(start);
            Assert.IsTrue(retry.Accepted);
            Assert.IsTrue(retry.DuplicateDelivery);
            Assert.AreEqual(revision, h.Service.Revision);

            CommandReply stale = h.Service.Apply(SessionCommand.Of(CommandType.RosterSet, revision - 1));
            Assert.AreEqual(RejectReason.STALE_REVISION, stale.Reason);

            CommandReply wrong = h.Send(CommandType.Arm);
            Assert.AreEqual(RejectReason.WRONG_STATE, wrong.Reason);
            Assert.AreEqual(revision, h.Service.Revision);
        }

        [Test]
        public void Roster_RejectsInvalidNames()
        {
            var h = new SessionHarness();
            h.Expect(CommandType.Start);
            Assert.IsFalse(h.Send(CommandType.RosterSet, c => c.Names = new List<string>()).Accepted);
            Assert.IsFalse(h.Send(CommandType.RosterSet, c => c.Names = new List<string> { "A", "B", "C", "D", "E", "F", "G" }).Accepted);
            Assert.IsFalse(h.Send(CommandType.RosterSet, c => c.Names = new List<string> { "   " }).Accepted);
            Assert.IsTrue(h.Send(CommandType.RosterSet, c => c.Names = new List<string> { "Caleb" }).Accepted);
        }

        [Test]
        public void Arm_RefusedWhenCabinetUnavailable()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Emulator.SetCommissioned(false);
            h.Pump();
            CommandReply reply = h.Send(CommandType.Arm);
            Assert.AreEqual(RejectReason.DEVICE_NOT_READY, reply.Reason);

            h.Emulator.SetCommissioned(true);
            h.Emulator.HeartbeatSuppressed = true;
            h.Clock.Advance(SessionCoordinator.HeartbeatTimeoutMs + 10);
            h.Pump();
            Assert.AreEqual(RejectReason.DEVICE_NOT_READY, h.Send(CommandType.Arm).Reason);
        }

        [Test]
        public void EventWhileNotArmed_IsRejected_AndCountsNothing()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Emulator.TriggerLane(10);
            h.Pump();
            Assert.AreEqual(InputRejection.NotArmed, h.Acks[h.Acks.Count - 1].Rejection);
            Assert.AreEqual(0, h.Snap.CurrentProgress.StrokesSoFar);
            Assert.AreEqual(0, h.Snap.Shots.Count);
        }

        [Test]
        public void DuplicateEvent_ProducesOneShot()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb", "Maya");
            h.Arm();
            h.Clock.Advance(700);
            h.Emulator.DuplicateNext = true;
            h.Emulator.TriggerLane(10);
            h.Pump();
            h.Clock.Advance(5);
            h.Pump();

            Assert.AreEqual(2, h.Acks.Count);
            Assert.IsTrue(h.Acks[0].Accepted && !h.Acks[0].Duplicate);
            Assert.IsTrue(h.Acks[1].Accepted && h.Acks[1].Duplicate);
            Assert.AreEqual(h.Acks[0].ShotId, h.Acks[1].ShotId);
            Assert.AreEqual(1, h.Snap.Shots.Count);
            Assert.AreEqual(1, h.Snap.ProgressFor(0).Played);
        }

        [Test]
        public void LateEvent_BeforeArmEpoch_IsRejected()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Arm();
            h.Clock.Advance(300);
            h.Emulator.InjectLateEvent(10, 5000);
            h.Pump();
            Assert.AreEqual(InputRejection.OutsideArmInterval, h.Acks[h.Acks.Count - 1].Rejection);
            Assert.AreEqual(SessionState.SHOT_ARMED, h.Service.State);
            Assert.AreEqual(0, h.Snap.Shots.Count);
        }

        [Test]
        public void ConflictingEvents_OnlyFirstIsAccepted()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Arm();
            h.Clock.Advance(500);
            h.Emulator.InjectConflict(7, 8);
            h.Clock.Advance(10);
            h.Pump();
            Assert.AreEqual(1, h.Snap.Shots.Count);
            Assert.AreEqual(7, h.Snap.LastShot.Intent.Lane);
            Assert.AreEqual(InputRejection.NotArmed, h.Acks[h.Acks.Count - 1].Rejection);
        }

        [Test]
        public void ControllerReboot_DisarmsAndRejectsOldBoot()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Arm();
            h.Emulator.RebootController();
            h.Pump();
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
            Assert.IsNotNull(h.Snap.Data.FaultReason);
            Assert.AreEqual(0, h.Snap.Shots.Count);
        }

        [Test]
        public void Meter_PowerComesFromCaptureTime_NotDeliveryTime()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Arm();
            ArmState arm = h.Snap.Data.Arm;
            h.Emulator.DelayMs = 250;
            h.Clock.AdvanceTo(arm.EpochMs + 0.5 * arm.SweepSeconds * 1000);
            h.Emulator.TriggerLane(12);
            h.Pump();
            Assert.AreEqual(SessionState.SHOT_ARMED, h.Service.State, "Delivery is delayed; nothing yet.");
            h.Clock.Advance(260);
            h.Pump();
            Assert.AreEqual(0.5, h.Snap.LastShot.Intent.Power01, 1e-9);
        }

        [Test]
        public void Water_FirstShot_ScoresTwo_DropsBall_NextLabelStrokeThree()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.AimAt(LakeCenter);
            h.Club("iron5");
            double power = h.FindPower(10, r => r.Outcome == ShotOutcome.Water, 0.01, 0.3);
            Assert.IsFalse(double.IsNaN(power), "No power found that reaches the lake.");

            InputAck ack = h.Shoot(10, power);
            Assert.IsTrue(ack.Accepted);
            ShotRecord shot = h.Snap.LastShot;
            Assert.AreEqual(ShotOutcome.Water, shot.Result.Outcome);
            Assert.IsNotNull(shot.Penalty);
            Assert.AreEqual(PenaltyType.Water, shot.Penalty.Type);

            HoleProgress progress = h.Snap.CurrentProgress;
            Assert.AreEqual(2, progress.StrokesSoFar);
            Assert.AreEqual(3, ScoreEngine.NextStrokeNumber(progress));
            Assert.IsNull(h.Service.World.HazardAt(progress.Ball.Position.x, progress.Ball.Position.z), "Ball must be dropped out of the hazard.");
            Assert.IsTrue(h.Service.World.InBounds(progress.Ball.Position.x, progress.Ball.Position.z));
        }

        [Test]
        public void OutOfBounds_ReplaysFromOrigin_WithOneStrokePenalty()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.AimAt(new Vec3d(80, 0, 90));
            h.Club("driver");
            double power = h.FindPower(20, r => r.Outcome == ShotOutcome.OutOfBounds, 0.02, 0.3);
            Assert.IsFalse(double.IsNaN(power), "No power found that goes out of bounds.");

            Vec3d origin = h.Snap.CurrentProgress.Ball.Position;
            h.Shoot(20, power);
            HoleProgress progress = h.Snap.CurrentProgress;
            Assert.AreEqual(ShotOutcome.OutOfBounds, h.Snap.LastShot.Result.Outcome);
            Assert.AreEqual(2, progress.StrokesSoFar);
            Assert.AreEqual(origin.x, progress.Ball.Position.x, 1e-6);
            Assert.AreEqual(origin.z, progress.Ball.Position.z, 1e-6);
        }

        [Test]
        public void TwoPlayers_CompleteTheHole_WithHoledAndCappedScores()
        {
            var h = new SessionHarness(course: FirstHoleOnly());
            h.StartRound("Caleb", "Maya");
            HoleDefinition hole = h.Snap.Hole;

            // Caleb: approach onto the green, then putt until holed.
            Assert.AreEqual("Caleb", h.Snap.CurrentPlayer.Name);
            h.AimAt(hole.CupM);
            double approach = h.FindPower(10, r => r.Outcome == ShotOutcome.Rest && r.FinalLie == SurfaceId.Green, 0.005, 0.4);
            Assert.IsFalse(double.IsNaN(approach), "No approach power reaches the green.");
            h.Shoot(10, approach);
            Assert.AreEqual(SurfaceId.Green, h.Snap.LastShot.Result.FinalLie);
            h.FinishShot();

            // Maya: soft shots until the pace cap (par 3 + 5 = MAX 8). Caleb putts between her strokes.
            int guard = 0;
            while (h.Service.State != SessionState.HOLE_COMPLETE && guard++ < 40)
            {
                PlayerState player = h.Snap.CurrentPlayer;
                if (player.Name == "Caleb")
                {
                    HoleProgress calebProgress = h.Snap.CurrentProgress;
                    Assert.AreEqual(HoleStatus.ACTIVE, calebProgress.Status, "A holed player must never get another turn.");
                    h.Club(ClubCatalog.PutterId);
                    h.AimAt(hole.CupM);
                    PuttScale scale = PuttScaleFor(Vec3d.HorizontalDistance(calebProgress.Ball.Position, hole.CupM));
                    h.Expect(CommandType.SetPuttScale, c => c.PuttScale = scale);
                    double putt = h.FindPower(10, r => r.Outcome == ShotOutcome.Holed, 0.002, 0.02);
                    if (double.IsNaN(putt))
                        putt = h.FindPower(10, r => r.Outcome == ShotOutcome.Rest && Vec3d.HorizontalDistance(r.FinalPosition, hole.CupM) < 1.0, 0.002, 0.02);
                    Assert.IsFalse(double.IsNaN(putt), "No putt gets close to the cup.");
                    h.Shoot(10, putt);
                }
                else
                {
                    Assert.AreEqual(HoleStatus.ACTIVE, h.Snap.CurrentProgress.Status, "A capped player must never get another turn.");
                    h.Shoot(10, 0.06);
                }
                h.FinishShot();
            }

            Assert.AreEqual(SessionState.HOLE_COMPLETE, h.Service.State);
            HoleProgress caleb = h.Snap.ProgressFor(0);
            HoleProgress maya = h.Snap.ProgressFor(1);
            Assert.AreEqual(HoleStatus.HOLED, caleb.Status);
            Assert.AreEqual(caleb.Played + caleb.Penalties, caleb.CompletionScore);
            Assert.AreEqual(HoleStatus.CAPPED, maya.Status);
            Assert.AreEqual(8, maya.CompletionScore);
            Assert.AreEqual($"{maya.CompletionScore}M", h.Snap.Scorecard[1].Cells[0].Display);

            h.Expect(CommandType.HoleContinue);
            Assert.AreEqual(SessionState.ROUND_COMPLETE, h.Service.State);
            Assert.AreEqual(RoundCompletion.Complete, h.Snap.Data.Completion);
            Assert.AreEqual(caleb.CompletionScore, h.Snap.Scorecard[0].CompletedTotal);

            h.Expect(CommandType.ReturnToAttract);
            Assert.AreEqual(SessionState.ATTRACT, h.Service.State);
        }

        [Test]
        public void HoleContinue_LoadsTheNextHole_ThenBriefsIt()
        {
            var h = new SessionHarness();
            Assert.Greater(h.Course.Holes.Count, 1);
            h.StartRound("Caleb");
            // Short taps until the stroke cap ends the hole.
            for (int i = 0; i < 20 && h.Service.State == SessionState.SHOT_SETUP; i++)
            {
                h.Shoot(10, 0.06);
                h.FinishShot();
            }
            Assert.AreEqual(SessionState.HOLE_COMPLETE, h.Service.State);
            h.Expect(CommandType.HoleContinue);
            Assert.AreEqual(SessionState.HOLE_LOAD, h.Service.State);
            Assert.AreEqual(1, h.Snap.Data.HoleIndex);
            Assert.AreEqual(h.Course.Holes[1].Id, h.Snap.Hole.Id);
            h.Expect(CommandType.HoleReady);
            Assert.AreEqual(SessionState.BRIEFING, h.Service.State);
            h.Expect(CommandType.BriefingReady);
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
            Assert.AreEqual(h.Course.Holes[1].TeeM, h.Snap.CurrentProgress.Ball.Position);
        }

        /// <summary>The authored course cut down to its first hole, so a round ends after one hole.</summary>
        private static CourseDefinition FirstHoleOnly()
        {
            CourseDefinition course = GrayboxCourse.Create();
            course.Holes.RemoveRange(1, course.Holes.Count - 1);
            ContentHasher.Seal(course);
            return course;
        }

        [Test]
        public void Pause_DisarmsAndResumeReturnsToSetup()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb");
            h.Arm();
            h.Expect(CommandType.Pause);
            Assert.AreEqual(SessionState.PAUSED, h.Service.State);
            Assert.IsNull(h.Snap.Data.Arm);
            h.Emulator.TriggerLane(10);
            h.Pump();
            Assert.AreEqual(0, h.Snap.Shots.Count);
            h.Expect(CommandType.Resume);
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
        }

        [Test]
        public void EndRound_Early_IsIncomplete()
        {
            var h = new SessionHarness();
            h.StartRound("Caleb", "Maya");
            h.Expect(CommandType.EndRound);
            Assert.AreEqual(SessionState.ROUND_COMPLETE, h.Service.State);
            Assert.AreEqual(RoundCompletion.Incomplete, h.Snap.Data.Completion);
        }

        [Test]
        public void StorageFailure_OnReservation_CountsNoStroke()
        {
            var store = new InMemorySessionStore();
            var h = new SessionHarness(store);
            h.StartRound("Caleb");
            h.Arm();
            h.Clock.Advance(600);
            store.FailNextCommit = true;
            h.Emulator.TriggerLane(10);
            h.Pump();
            Assert.AreEqual(InputRejection.StorageFailure, h.Acks[h.Acks.Count - 1].Rejection);
            Assert.AreEqual(SessionState.SHOT_SETUP, h.Service.State);
            Assert.AreEqual(0, h.Snap.CurrentProgress.StrokesSoFar);
            Assert.AreEqual(0, h.Snap.Shots.Count);
        }

        private static PuttScale PuttScaleFor(double meters)
        {
            double feet = Units.MetersToFeet(meters);
            if (feet <= 12) return PuttScale.Short;
            if (feet <= 34) return PuttScale.Medium;
            return PuttScale.Long;
        }
    }
}
