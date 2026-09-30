using System;
using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Session
{
    public enum CommandType
    {
        Start,
        RosterSet,
        SettingsConfirm,
        HoleReady,
        BriefingReady,
        SelectClub,
        SetAim,
        SetPuttScale,
        Arm,
        Disarm,
        PresentationBegin,
        PresentationSkip,
        PresentationEnd,
        ResultContinue,
        NextPlayerReady,
        HoleContinue,
        Pause,
        Resume,
        EndRound,
        ReturnToAttract,
        /// <summary>Pro Shop save: replaces one player's cosmetic loadout. Lobby or paused only.</summary>
        SetLoadout,
    }

    public enum RejectReason
    {
        None,
        STALE_REVISION,
        WRONG_STATE,
        DEVICE_NOT_READY,
        INVALID_VALUE,
        SESSION_NOT_FOUND,
        COMMAND_ID_CONFLICT,
        STORAGE_FAILURE,
    }

    /// <summary>
    /// Semantic player command (spec section 37). Idempotent by CommandId within a session; every command states the
    /// revision it was issued against so stale UI interactions are rejected.
    /// </summary>
    [Serializable]
    public sealed class SessionCommand
    {
        public string CommandId;
        public long ExpectedRevision;
        public CommandType Type;

        public List<string> Names;
        public AssistanceProfileId? Assistance;
        public bool? PaceCapEnabled;
        public string ClubId;
        /// <summary>Rotate the aim target around the ball by this many radians (positive = right).</summary>
        public double? AimYawDeltaRad;
        public Vec3d? AimTargetM;
        public PuttScale? PuttScale;
        public int? PlayerId;
        public Loadout Loadout;

        public static SessionCommand Of(CommandType type, long expectedRevision) => new SessionCommand
        {
            CommandId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = expectedRevision,
            Type = type,
        };
    }

    public sealed class CommandReply
    {
        public bool Accepted;
        public long Revision;
        public RejectReason Reason;
        public string Detail;
        public bool DuplicateDelivery;

        public static CommandReply Ok(long revision, bool duplicate = false) =>
            new CommandReply { Accepted = true, Revision = revision, DuplicateDelivery = duplicate };

        public static CommandReply Reject(RejectReason reason, long revision, string detail = null) =>
            new CommandReply { Accepted = false, Reason = reason, Revision = revision, Detail = detail };

        public override string ToString() => Accepted ? $"accepted r{Revision}" : $"rejected {Reason}: {Detail}";
    }

    public enum CrashPoint
    {
        None,
        AfterReservation,
        AfterResolution,
    }

    /// <summary>Thrown by the debug crash hook after a transaction commits, to prove recovery.</summary>
    public sealed class SimulatedCrashException : Exception
    {
        public CrashPoint Point { get; }

        public SimulatedCrashException(CrashPoint point) : base($"Simulated crash {point}.")
        {
            Point = point;
        }
    }
}
