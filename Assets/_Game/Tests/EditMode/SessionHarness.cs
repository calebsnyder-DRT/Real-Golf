using System;
using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Hardware;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Session;
using Puttshack.Simulation;
using Puttshack.Storage;
using Puttshack.Time;

namespace Puttshack.Tests
{
    /// <summary>Drives the real service through the emulator with a virtual clock, exactly as the host does.</summary>
    public sealed class SessionHarness
    {
        public readonly CourseDefinition Course;
        public readonly VirtualClock Clock = new VirtualClock(1000);
        public readonly ISessionStore Store;
        public readonly EmulatorCabinetAdapter Emulator;
        public SessionCoordinator Service;
        public readonly List<InputAck> Acks = new List<InputAck>();
        public SimulatedCrashException LastCrash;

        public SessionHarness(ISessionStore store = null, CourseDefinition course = null)
        {
            Course = course ?? GrayboxCourse.Create();
            Store = store ?? new InMemorySessionStore();
            Emulator = new EmulatorCabinetAdapter(Clock);
            Service = SessionCoordinator.StartOrResume(Course, Store, Clock);
            Service.UpdateCabinetHealth(Emulator.Health);
        }

        public SessionSnapshot Snap => Service.Snapshot;

        /// <summary>Simulates a process restart: a new service instance loads from the same durable store.</summary>
        public void Restart()
        {
            Service = SessionCoordinator.StartOrResume(Course, Store, Clock);
            Service.UpdateCabinetHealth(Emulator.Health);
        }

        public void Pump()
        {
            IReadOnlyList<CabinetEvent> events = Emulator.Poll(Clock.NowMs);
            Service.UpdateCabinetHealth(Emulator.Health);
            foreach (CabinetEvent evt in events)
            {
                try
                {
                    Acks.Add(Service.HandleCabinetEvent(evt));
                }
                catch (SimulatedCrashException crash)
                {
                    LastCrash = crash;
                    return;
                }
            }
            try
            {
                Service.Tick();
            }
            catch (SimulatedCrashException crash)
            {
                LastCrash = crash;
            }
        }

        public CommandReply Send(CommandType type, Action<SessionCommand> configure = null)
        {
            SessionCommand cmd = SessionCommand.Of(type, Service.Revision);
            configure?.Invoke(cmd);
            return Service.Apply(cmd);
        }

        public void Expect(CommandType type, Action<SessionCommand> configure = null)
        {
            CommandReply reply = Send(type, configure);
            Assert.IsTrue(reply.Accepted, $"{type} in {Service.State}: {reply}");
        }

        public void StartRound(params string[] names)
        {
            Expect(CommandType.Start);
            Expect(CommandType.RosterSet, c => c.Names = new List<string>(names));
            Expect(CommandType.SettingsConfirm, c => c.Assistance = AssistanceProfileId.Club);
            Expect(CommandType.HoleReady);
            Expect(CommandType.BriefingReady);
            Assert.AreEqual(SessionState.SHOT_SETUP, Service.State);
        }

        public void Arm()
        {
            Expect(CommandType.Arm);
            Pump();
            Assert.AreEqual(SessionState.SHOT_ARMED, Service.State);
        }

        /// <summary>Arms and lands a ball in <paramref name="lane"/> at the moment the meter reads <paramref name="power"/> (rising half).</summary>
        public InputAck Shoot(int lane, double power)
        {
            Arm();
            ArmState arm = Service.Snapshot.Data.Arm;
            Clock.AdvanceTo(arm.EpochMs + power * arm.SweepSeconds * 1000.0);
            Emulator.TriggerLane(lane);
            int before = Acks.Count;
            Pump();
            return Acks.Count > before ? Acks[Acks.Count - 1] : null;
        }

        /// <summary>Presentation and result acknowledgement, the way the host advances after a resolved shot.</summary>
        public void FinishShot()
        {
            Assert.AreEqual(SessionState.SHOT_RESOLVED, Service.State);
            Expect(CommandType.PresentationBegin);
            Expect(CommandType.PresentationEnd);
            Expect(CommandType.ResultContinue);
            if (Service.State == SessionState.HANDOFF) Expect(CommandType.NextPlayerReady);
        }

        public void AimAt(Vec3d target) => Expect(CommandType.SetAim, c => c.AimTargetM = target);

        public void Club(string id) => Expect(CommandType.SelectClub, c => c.ClubId = id);

        /// <summary>Predicts the committed outcome using the same resolver math and solver the service uses.</summary>
        public ShotResult Predict(int lane, double power)
        {
            SessionSnapshot snap = Service.Snapshot;
            HoleProgress progress = snap.CurrentProgress;
            ClubDefinition club = ClubCatalog.Get(snap.Data.Setup.ClubId);
            double raw = LaneMapper.RawYawRad(lane, Units.DegToRad(club.FanHalfAngleDeg));
            var intent = new ShotIntent
            {
                ClubId = club.Id,
                ShotType = club.ShotType,
                PuttScale = snap.Data.Setup.PuttScale,
                OriginM = progress.Ball.Position,
                AimForward = (snap.Data.Setup.AimTargetM - progress.Ball.Position).Horizontal.Normalized,
                Lie = progress.Ball.Lie,
                WindMps = snap.Hole.WindMps,
                Power01 = power,
                RawYawRad = raw,
                YawRad = Assistance.ApplyYaw(raw, 0, AssistanceProfiles.Get(snap.Data.Settings.Assistance)),
            };
            return ShotSolver.Solve(intent, Service.World);
        }

        /// <summary>
        /// Searches meter power for an outcome and returns the middle of the first accepted run, so floating-point
        /// differences in capture time cannot flip the result. The power is then played through the real pipeline.
        /// </summary>
        public double FindPower(int lane, Func<ShotResult, bool> accept, double step = 0.005, double from = 0.02, double to = 1.0)
        {
            double first = double.NaN, last = double.NaN;
            for (double p = from; p <= to + 1e-9; p += step)
            {
                bool ok = accept(Predict(lane, p));
                if (ok)
                {
                    if (double.IsNaN(first)) first = p;
                    last = p;
                }
                else if (!double.IsNaN(first)) break;
            }
            return double.IsNaN(first) ? double.NaN : (first + last) / 2;
        }
    }
}
