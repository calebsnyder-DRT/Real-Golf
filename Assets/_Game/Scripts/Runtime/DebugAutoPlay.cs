using System;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Session;
using Puttshack.Simulation;

namespace Puttshack.Runtime
{
    /// <summary>
    /// Development input: the meter resolver, except that the next accepted event can carry a chosen power, so debug
    /// tools play real strokes through the normal pipeline (journal, solver, rules) without waiting on the meter sweep.
    /// With no power queued it is exactly <see cref="MeterPowerResolver"/>.
    /// </summary>
    public sealed class DebugPowerResolver : IInputResolver
    {
        private readonly MeterPowerResolver _meter = new MeterPowerResolver();

        public double? NextPower { get; set; }
        public string InputMode => _meter.InputMode;

        public InputRejection Validate(CabinetEvent evt, ArmState arm, double receivedMs) => _meter.Validate(evt, arm, receivedMs);

        public ShotIntent Resolve(CabinetEvent evt, ArmState arm, ResolveContext context)
        {
            ShotIntent intent = _meter.Resolve(evt, arm, context);
            if (NextPower.HasValue)
            {
                intent.Power01 = NextPower.Value;
                NextPower = null;
            }
            return intent;
        }
    }

    /// <summary>
    /// Development fast-forward through the round (F9: finish this hole, SHIFT+F9: play on to the last hole's result).
    /// Every stroke goes through the real commands and the emulator with the power the solver says finishes nearest the
    /// cup, plus a little scatter so players' scores differ. Scores, journal and integrity checks are all genuine.
    /// </summary>
    public static class DebugAutoPlay
    {
        private const int MaxStepsPerHole = 600;
        private const double PowerScatter = 0.08;

        public static bool FinishHole(GameHost host, Random rng)
        {
            for (int i = 0; i < MaxStepsPerHole; i++)
            {
                if (host.Coordinator.State == SessionState.HOLE_COMPLETE) return true;
                if (!Step(host, rng)) return false;
            }
            return false;
        }

        /// <summary>Plays every remaining hole and stops on the last one's HOLE_COMPLETE, so the finish is presented.</summary>
        public static bool FinishRound(GameHost host, Random rng)
        {
            for (int i = 0; i < MaxStepsPerHole * CourseRouting.FullRound; i++)
            {
                SessionSnapshot snap = host.Coordinator.Snapshot;
                if (snap.State == SessionState.HOLE_COMPLETE)
                {
                    if (snap.Data.HoleIndex >= snap.Data.HoleCount - 1) return true;
                    if (!Accepted(host.Send(CommandType.HoleContinue))) return false;
                    continue;
                }
                if (!Step(host, rng)) return false;
            }
            return false;
        }

        /// <summary>One transition toward the end of the current hole.</summary>
        private static bool Step(GameHost host, Random rng)
        {
            switch (host.Coordinator.State)
            {
                case SessionState.HOLE_LOAD: return Accepted(host.Send(CommandType.HoleReady));
                case SessionState.BRIEFING: return Accepted(host.Send(CommandType.BriefingReady));
                case SessionState.SHOT_SETUP: return Stroke(host, rng);
                case SessionState.ARMING:
                case SessionState.SHOT_ARMED: return Accepted(host.Send(CommandType.Disarm));
                case SessionState.SHOT_RESOLVED: return Accepted(host.Send(CommandType.PresentationBegin));
                case SessionState.SHOT_PRESENTING:
                    host.SkipPresentation();
                    return host.Coordinator.State != SessionState.SHOT_PRESENTING;
                case SessionState.SHOT_RESULT: return Accepted(host.Send(CommandType.ResultContinue));
                case SessionState.HANDOFF: return Accepted(host.Send(CommandType.NextPlayerReady));
                default: return false;
            }
        }

        private static bool Stroke(GameHost host, Random rng)
        {
            DebugPowerResolver input = host.DebugInput;
            if (input == null) return false;
            SessionCoordinator service = host.Coordinator;
            int lane = 9 + rng.Next(4);
            double power = BestPower(service, service.Snapshot, lane);
            power = Math.Max(0.02, Math.Min(1.0, power * (1 + (rng.NextDouble() - 0.5) * 2 * PowerScatter)));

            if (!Accepted(host.Send(CommandType.Arm))) return false;
            service.Tick();
            if (service.State != SessionState.SHOT_ARMED) return false;
            input.NextPower = power;
            host.Emulator.TriggerLane(lane);
            foreach (CabinetEvent evt in host.Emulator.Poll(host.Clock.NowMs)) service.HandleCabinetEvent(evt);
            service.UpdateCabinetHealth(host.Emulator.Health);
            input.NextPower = null;
            return service.State == SessionState.SHOT_RESOLVED;
        }

        /// <summary>Coarse-then-fine search of meter power for the finish nearest the cup, avoiding penalty outcomes.</summary>
        private static double BestPower(SessionCoordinator service, SessionSnapshot snap, int lane)
        {
            HoleProgress progress = snap.CurrentProgress;
            ClubDefinition club = ClubCatalog.Get(snap.Data.Setup.ClubId);
            AssistanceProfile profile = AssistanceProfiles.Get(snap.Data.Settings.Assistance);
            double raw = LaneMapper.RawYawRad(lane, Units.DegToRad(club.FanHalfAngleDeg));
            Vec3d cup = snap.Hole.CupM;

            double Miss(double power)
            {
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
                    YawRad = Assistance.ApplyYaw(raw, 0, profile),
                };
                ShotResult r = ShotSolver.Solve(intent, service.World);
                if (r.Outcome == ShotOutcome.Holed) return 0;
                double miss = Vec3d.HorizontalDistance(r.FinalPosition, cup);
                if (r.Outcome == ShotOutcome.Water || r.Outcome == ShotOutcome.OutOfBounds || r.Outcome == ShotOutcome.SolverFault) miss += 1000;
                return miss;
            }

            double best = 0.5, bestMiss = double.MaxValue;
            for (double p = 0.05; p <= 1.0001; p += 0.05) Consider(p);
            double lo = Math.Max(0.02, best - 0.05), hi = Math.Min(1.0, best + 0.05);
            for (double p = lo; p <= hi + 1e-9; p += 0.01) Consider(p);
            return best;

            void Consider(double p)
            {
                double miss = Miss(p);
                if (miss >= bestMiss) return;
                bestMiss = miss;
                best = p;
            }
        }

        private static bool Accepted(CommandReply reply) => reply != null && reply.Accepted;
    }
}
