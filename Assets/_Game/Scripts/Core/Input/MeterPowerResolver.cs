using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Protocol;

namespace Puttshack.Input
{
    /// <summary>Inputs the resolver needs beyond the event and arm snapshot.</summary>
    public sealed class ResolveContext
    {
        public int SessionId;
        public long SessionRevision;
        public int ShotId;
        public string HoleId;
        public string HoleVersion;
        public Vec3d WindMps;
        public AssistanceProfileId AssistanceProfile;
    }

    /// <summary>
    /// A lane event freezes the meter at its capture time and selects direction within the frozen aim fan (spec section 10).
    /// A future SpeedPowerResolver can replace this while preserving ShotIntent.
    /// </summary>
    public interface IInputResolver
    {
        string InputMode { get; }
        InputRejection Validate(CabinetEvent evt, ArmState arm, double receivedMs);
        ShotIntent Resolve(CabinetEvent evt, ArmState arm, ResolveContext context);
    }

    public sealed class MeterPowerResolver : IInputResolver
    {
        public string InputMode => "meter";

        public InputRejection Validate(CabinetEvent evt, ArmState arm, double receivedMs)
        {
            if (arm == null) return InputRejection.NotArmed;
            if (evt.DeviceId != arm.DeviceId) return InputRejection.UnknownDevice;
            if (evt.BootId != arm.BootId) return InputRejection.StaleBoot;
            if (!LaneMapper.IsValidLane(evt.LogicalLane)) return InputRejection.LaneOutOfRange;
            if (evt.CalibrationVersion != arm.CalibrationVersion) return InputRejection.CalibrationMismatch;
            if ((evt.Quality & EventQuality.ClockUncertain) != 0) return InputRejection.ClockInvalid;
            double capture = evt.EffectiveCaptureMs;
            if (double.IsNaN(capture) || double.IsInfinity(capture)) return InputRejection.ClockInvalid;
            if (capture < arm.EpochMs || capture > receivedMs + 1.0) return InputRejection.OutsideArmInterval;
            return InputRejection.None;
        }

        public ShotIntent Resolve(CabinetEvent evt, ArmState arm, ResolveContext context)
        {
            double elapsedMs = evt.EffectiveCaptureMs - arm.EpochMs;
            double power = MeterWaveform.Power(elapsedMs, arm.SweepSeconds);
            double rawYaw = LaneMapper.RawYawRad(evt.LogicalLane, arm.FanHalfAngleRad);
            double yaw = Assistance.ApplyYaw(rawYaw, 0, AssistanceProfiles.Get(context.AssistanceProfile));

            return new ShotIntent
            {
                SessionId = context.SessionId,
                SessionRevision = context.SessionRevision,
                TurnId = arm.TurnId,
                ShotId = context.ShotId,
                PlayerId = arm.PlayerId,
                HoleId = context.HoleId,
                HoleVersion = context.HoleVersion,
                RulesVersion = Versions.Rules,
                SimulationVersion = Versions.Simulation,
                ArmToken = arm.Token,
                InputMode = InputMode,
                Source = new ShotSource { DeviceId = evt.DeviceId, BootId = evt.BootId, Seq = evt.Sequence },
                CalibrationVersion = evt.CalibrationVersion,
                CapturedHostMs = evt.EffectiveCaptureMs,
                Lane = evt.LogicalLane,
                Power01 = power,
                ClubId = arm.ClubId,
                ShotType = arm.ShotType,
                PuttScale = arm.PuttScale,
                OriginM = arm.OriginM,
                AimForward = arm.AimForward,
                Lie = arm.Lie,
                WindMps = context.WindMps,
                AssistanceProfile = context.AssistanceProfile,
                RawYawRad = rawYaw,
                YawRad = yaw,
            };
        }
    }
}
