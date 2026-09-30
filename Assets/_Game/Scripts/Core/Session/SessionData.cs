using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Puttshack.Domain;

namespace Puttshack.Session
{
    /// <summary>
    /// Authoritative, serializable session state excluding shot records (those live in the shot ledger).
    /// Only <see cref="SessionCoordinator"/> mutates it, and only on a draft copy that is swapped in after a durable commit.
    /// </summary>
    [Serializable]
    public sealed class SessionData
    {
        public int SessionId;
        public long Revision;
        public SessionState State = SessionState.ATTRACT;
        /// <summary>State to return to when a PAUSED or FAULT overlay clears.</summary>
        public SessionState ResumeState;
        public string FaultReason;
        public RoundCompletion Completion = RoundCompletion.InProgress;

        public string CourseId;
        public string CourseVersion;
        public string CourseContentHash;
        public string RulesVersion = Versions.Rules;
        public string SimulationVersion = Versions.Simulation;
        public SessionSettings Settings = new SessionSettings();
        public bool SettingsFrozen;

        public List<PlayerState> Players = new List<PlayerState>();
        /// <summary>One entry per player per hole: index = rosterIndex * holeCount + holeIndex.</summary>
        public List<HoleProgress> Progress = new List<HoleProgress>();
        public int HoleCount;

        public int HoleIndex;
        public int CurrentRosterIndex;
        public int TurnId;
        public int NextTurnId = 1;
        public int NextShotId = 1;
        public int ArmSequence;

        public SetupState Setup = new SetupState();
        public ArmState Arm;
        public int? PendingShotId;
        public int? LastShotId;

        /// <summary>Consumed physical event identities (device|boot|seq) mapped to the shot they produced.</summary>
        public Dictionary<string, int> ConsumedEvents = new Dictionary<string, int>();
        /// <summary>Accepted command IDs with a payload fingerprint, for idempotent retries.</summary>
        public Dictionary<string, string> AppliedCommands = new Dictionary<string, string>();

        public HoleProgress ProgressFor(int rosterIndex, int holeIndex) => Progress[rosterIndex * HoleCount + holeIndex];

        [JsonIgnore]
        public PlayerState CurrentPlayer =>
            CurrentRosterIndex >= 0 && CurrentRosterIndex < Players.Count ? Players[CurrentRosterIndex] : null;

        [JsonIgnore]
        public HoleProgress CurrentProgress =>
            CurrentRosterIndex >= 0 && CurrentRosterIndex < Players.Count && HoleIndex < HoleCount
                ? ProgressFor(CurrentRosterIndex, HoleIndex)
                : null;
    }
}
