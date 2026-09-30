namespace Puttshack.Domain
{
    public enum SessionState
    {
        ATTRACT,
        LOBBY,
        TUTORIAL,
        HOLE_LOAD,
        BRIEFING,
        SHOT_SETUP,
        ARMING,
        SHOT_ARMED,
        SHOT_RESERVED,
        SHOT_RESOLVED,
        SHOT_PRESENTING,
        SHOT_RESULT,
        HANDOFF,
        HOLE_COMPLETE,
        ROUND_COMPLETE,
        PAUSED,
        FAULT,
    }

    public enum HoleStatus
    {
        NOT_STARTED,
        ACTIVE,
        HOLED,
        CAPPED,
        WITHDRAWN,
    }

    public enum ShotStatus
    {
        RESERVED,
        RESOLVED,
        VOIDED_BY_OPERATOR,
    }

    public enum AssistanceProfileId
    {
        Amateur,
        Club,
        Pro,
    }

    public enum ShotType
    {
        Full,
        Pitch,
        Putt,
    }

    public enum PuttScale
    {
        Short,
        Medium,
        Long,
    }

    public enum SurfaceId
    {
        Tee,
        Fairway,
        LightRough,
        DeepRough,
        Bunker,
        Green,
        Water,
        CartPath,
    }

    public enum ShotOutcome
    {
        Rest,
        Holed,
        Water,
        OutOfBounds,
        SolverFault,
    }

    public enum PenaltyType
    {
        None,
        Water,
        OutOfBounds,
    }

    public enum RoundCompletion
    {
        InProgress,
        Complete,
        Incomplete,
    }
}
