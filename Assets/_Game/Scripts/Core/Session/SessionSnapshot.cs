using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Rules;

namespace Puttshack.Session
{
    /// <summary>
    /// Immutable view handed to presentation. It is a detached copy, so the renderer cannot mutate authoritative state.
    /// </summary>
    public sealed class SessionSnapshot
    {
        public SessionData Data { get; }
        public CourseDefinition Course { get; }
        public IReadOnlyDictionary<int, ShotRecord> Shots { get; }
        public IReadOnlyList<ScorecardRow> Scorecard { get; }
        public bool CabinetReady { get; }
        public string CabinetStatus { get; }

        public long Revision => Data.Revision;
        public SessionState State => Data.State;
        public HoleDefinition Hole => Data.HoleIndex < Course.Holes.Count ? Course.Holes[Data.HoleIndex] : null;
        public PlayerState CurrentPlayer => Data.CurrentPlayer;
        public HoleProgress CurrentProgress => Data.State == SessionState.ATTRACT || Data.State == SessionState.LOBBY ? null : Data.CurrentProgress;

        public ShotRecord LastShot =>
            Data.LastShotId.HasValue && Shots.TryGetValue(Data.LastShotId.Value, out ShotRecord shot) ? shot : null;

        public ShotRecord PendingShot =>
            Data.PendingShotId.HasValue && Shots.TryGetValue(Data.PendingShotId.Value, out ShotRecord shot) ? shot : null;

        /// <summary>The state behind a PAUSED/FAULT overlay, or the state itself.</summary>
        public SessionState EffectiveState =>
            Data.State == SessionState.PAUSED || Data.State == SessionState.FAULT ? Data.ResumeState : Data.State;

        public SessionSnapshot(SessionData data, CourseDefinition course, IReadOnlyDictionary<int, ShotRecord> shots, bool cabinetReady, string cabinetStatus)
        {
            Data = data;
            Course = course;
            Shots = shots;
            CabinetReady = cabinetReady;
            CabinetStatus = cabinetStatus;

            var rows = new List<ScorecardRow>();
            List<int> pars = course.Pars;
            if (data.HoleCount > 0 && data.Progress.Count == data.Players.Count * data.HoleCount)
            {
                foreach (PlayerState player in data.Players)
                {
                    var progressByHole = new List<HoleProgress>(data.HoleCount);
                    for (int h = 0; h < data.HoleCount; h++) progressByHole.Add(data.ProgressFor(player.RosterIndex, h));
                    rows.Add(ScoreEngine.BuildRow(player.Id, player.Name, pars, progressByHole));
                }
            }
            Scorecard = rows;
        }

        public HoleProgress ProgressFor(int rosterIndex) =>
            Data.HoleCount > 0 && rosterIndex < Data.Players.Count ? Data.ProgressFor(rosterIndex, Data.HoleIndex) : null;

        /// <summary>Estimated meter power for display only; the accepted value comes from the capture time.</summary>
        public double MeterEstimate(double nowMs) =>
            Data.Arm == null ? Data.Settings.PreviewPower : Input.MeterWaveform.Power(nowMs - Data.Arm.EpochMs, Data.Arm.SweepSeconds);
    }
}
