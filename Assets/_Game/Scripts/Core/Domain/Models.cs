using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Puttshack.Domain
{
    public static class Versions
    {
        public const string Rules = "rules-1.0";
        public const string Simulation = "sim-1.0";
        public const int Protocol = 1;
        public const int IntentSchema = 1;
    }

    [Serializable]
    public sealed class PlayerState
    {
        public int Id;
        public int RosterIndex;
        public string Name;
        public int ColorIndex;
        public bool Withdrawn;
    }

    [Serializable]
    public sealed class BallState
    {
        public Vec3d Position;
        public SurfaceId Lie;
        /// <summary>Origin of the most recent stroke; out-of-bounds relief returns here.</summary>
        public Vec3d LastOrigin;
        public SurfaceId LastOriginLie;
    }

    [Serializable]
    public sealed class HoleProgress
    {
        public int PlayerId;
        public int HoleIndex;
        public HoleStatus Status;
        public int Played;
        public int Penalties;
        /// <summary>Raw count for HOLED; the disclosed cap value for CAPPED; null otherwise.</summary>
        public int? CompletionScore;
        public string StatusReason;
        public BallState Ball;

        [JsonIgnore]
        public int StrokesSoFar => Played + Penalties;
        [JsonIgnore]
        public bool IsComplete => Status == HoleStatus.HOLED || Status == HoleStatus.CAPPED;
    }

    [Serializable]
    public sealed class ShotSource
    {
        public string DeviceId;
        public string BootId;
        public long Seq;

        [JsonIgnore]
        public string Key => $"{DeviceId}|{BootId}|{Seq}";
    }

    /// <summary>Normalized, immutable request accepted from a real or emulated input (spec section 13).</summary>
    [Serializable]
    public sealed class ShotIntent
    {
        public int SchemaVersion = Versions.IntentSchema;
        public int SessionId;
        public long SessionRevision;
        public int TurnId;
        public int ShotId;
        public int PlayerId;
        public string HoleId;
        public string HoleVersion;
        public string RulesVersion;
        public string SimulationVersion;
        public string ArmToken;
        public string InputMode;
        public ShotSource Source;
        public string CalibrationVersion;
        public double CapturedHostMs;
        public int Lane;
        public double Power01;
        public string ClubId;
        public ShotType ShotType;
        public PuttScale PuttScale;
        public Vec3d OriginM;
        public Vec3d AimForward;
        public SurfaceId Lie;
        public Vec3d WindMps;
        public AssistanceProfileId AssistanceProfile;
        /// <summary>Yaw after assistance, relative to AimForward. Stored so replay needs no profile lookup.</summary>
        public double YawRad;
        public double RawYawRad;
    }

    [Serializable]
    public struct TrajectorySample
    {
        public double t;
        public Vec3d p;

        public TrajectorySample(double t, Vec3d p)
        {
            this.t = t;
            this.p = p;
        }
    }

    public enum ShotEventType
    {
        Launch,
        FirstContact,
        Bounce,
        TreeHit,
        StartRoll,
        LipOut,
        Cup,
        WaterEntry,
        OutOfBounds,
        Rest,
        Fault,
    }

    [Serializable]
    public struct ShotEvent
    {
        public double t;
        public ShotEventType Type;
        public Vec3d p;
        public SurfaceId Surface;

        public ShotEvent(double t, ShotEventType type, Vec3d p, SurfaceId surface)
        {
            this.t = t;
            Type = type;
            this.p = p;
            Surface = surface;
        }
    }

    [Serializable]
    public sealed class ShotResult
    {
        public ShotOutcome Outcome;
        public Vec3d FinalPosition;
        public SurfaceId FinalLie;
        public double CarryM;
        public double RollM;
        public double TotalM;
        public double ApexM;
        public double DurationS;
        /// <summary>Last point on playable ground before a hazard or boundary crossing.</summary>
        public Vec3d CrossingPosition;
        public string HazardId;
        public List<TrajectorySample> Samples = new List<TrajectorySample>();
        public List<ShotEvent> Events = new List<ShotEvent>();
        public string ResultHash;
    }

    [Serializable]
    public sealed class PenaltyRecord
    {
        public PenaltyType Type;
        public int Strokes;
        public string HazardId;
        public Vec3d OriginalPosition;
        public Vec3d ReliefPosition;
        public SurfaceId ReliefLie;
        public bool UsedFallback;
        public string Explanation;
    }

    [Serializable]
    public sealed class ShotRecord
    {
        public int ShotId;
        public int PlayerId;
        public int HoleIndex;
        public int TurnId;
        public int StrokeNumber;
        public ShotStatus Status;
        public string EventKey;
        public ShotIntent Intent;
        public ShotResult Result;
        public PenaltyRecord Penalty;
        /// <summary>Hole status after this shot resolved, so the result screen can explain the transition.</summary>
        public HoleStatus HoleStatusAfter;
        public int ScoreSoFarAfter;
    }

    [Serializable]
    public sealed class SessionSettings
    {
        public AssistanceProfileId Assistance = AssistanceProfileId.Club;
        public bool PaceCapEnabled = true;
        public int PaceCapOverPar = 5;
        public string InputMode = "meter";
        public double PreviewPower = 0.75;
    }

    [Serializable]
    public sealed class SetupState
    {
        public string ClubId;
        public Vec3d AimTargetM;
        public PuttScale PuttScale = PuttScale.Medium;
    }

    /// <summary>Everything frozen at arm time. The meter and aim cannot change after this exists.</summary>
    [Serializable]
    public sealed class ArmState
    {
        public string Token;
        public double EpochMs;
        public double SweepSeconds;
        public string DeviceId;
        public string BootId;
        public string CalibrationVersion;
        public int TurnId;
        public int PlayerId;
        public string ClubId;
        public ShotType ShotType;
        public PuttScale PuttScale;
        public Vec3d OriginM;
        public Vec3d AimForward;
        public SurfaceId Lie;
        public double FanHalfAngleRad;
    }
}
