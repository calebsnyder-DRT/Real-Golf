using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Session;
using UnityEngine;

namespace Puttshack.Runtime.Audio
{
    /// <summary>
    /// The gallery and the round's beats: crowd reactions keyed to each shot's committed outcome (score to par when
    /// holed, distance to the pin otherwise), a hole-out stinger per player, and the cues for the round flow's pages.
    /// </summary>
    public sealed partial class GolfAudioDirector
    {
        private SessionState _flowState = (SessionState)(-1);
        private int _flowHole = -1, _flowSession = -1;
        private int _clubPlayer = -1;
        private string _clubId;

        private void ReactToShot(ShotRecord shot, bool lipped)
        {
            ShotResult r = shot.Result;
            if (r == null || shot.Intent == null || _host == null) return;
            CourseDefinition course = _host.Coordinator.Snapshot.Course;
            HoleDefinition hole = shot.HoleIndex >= 0 && shot.HoleIndex < course.Holes.Count ? course.Holes[shot.HoleIndex] : null;
            if (hole == null) return;

            CrowdSettings c = _bank.Crowd;
            float d = c.ReactionDelay;
            bool putt = shot.Intent.ClubId == ClubCatalog.PutterId;
            float left = (float)Vec3d.HorizontalDistance(r.FinalPosition, hole.CupM);
            float from = (float)Vec3d.HorizontalDistance(shot.Intent.OriginM, hole.CupM);

            switch (r.Outcome)
            {
                case ShotOutcome.Holed:
                {
                    int toPar = shot.ScoreSoFarAfter - hole.Par;
                    bool ace = shot.ScoreSoFarAfter == 1;
                    if (ace)
                    {
                        React(d, GolfSound.Cheer);
                        React(d + 0.4f, GolfSound.Ovation);
                    }
                    else if (!putt || toPar <= -2)
                    {
                        React(d, GolfSound.Cheer);
                        React(d + 0.3f, GolfSound.ApplauseBig);
                    }
                    else if (toPar == -1 || from > c.LongPuttM)
                    {
                        React(d, GolfSound.ApplauseBig);
                        if (toPar == -1) React(d + 0.15f, GolfSound.Cheer, 0.6f);
                    }
                    else
                    {
                        React(d, GolfSound.ApplauseSmall, toPar == 0 ? 1f : 0.6f);
                    }
                    if (shot.HoleStatusAfter == HoleStatus.HOLED) React(d + 0.5f, HoleOutStinger(ace, toPar));
                    break;
                }
                case ShotOutcome.Water:
                case ShotOutcome.OutOfBounds:
                    React(d, GolfSound.CrowdAww);
                    break;
                case ShotOutcome.Rest:
                    if (lipped) break;
                    if (putt)
                    {
                        if (left < c.NearMissM && from > 1.5f) React(d, GolfSound.CrowdOoh, 0.8f);
                        break;
                    }
                    if (r.FinalLie == SurfaceId.Green && left < c.CloseApproachM) React(d, GolfSound.ApplauseBig);
                    else if (r.FinalLie == SurfaceId.Green && left < c.GoodApproachM) React(d, GolfSound.ApplauseSmall);
                    else if (shot.StrokeNumber == 1 && hole.Par >= 4 && r.FinalLie == SurfaceId.Fairway && r.TotalM >= c.LongDriveM)
                        React(d, GolfSound.ApplauseSmall, 0.7f);
                    break;
            }
        }

        private void React(float delay, GolfSound id, float volume = 1f) => Schedule(delay, () => PlayFlat(id, volume));

        private static GolfSound HoleOutStinger(bool ace, int toPar)
        {
            if (ace) return GolfSound.StingerAce;
            if (toPar <= -2) return GolfSound.StingerEagle;
            if (toPar == -1) return GolfSound.StingerBirdie;
            if (toPar == 0) return GolfSound.StingerPar;
            return toPar == 1 ? GolfSound.StingerBogey : GolfSound.StingerDouble;
        }

        /// <summary>One-shots on the round flow's state changes (not on the first frame bound, so a resumed round is quiet).</summary>
        private void UpdateFlow(SessionSnapshot snap)
        {
            SessionState state = snap.EffectiveState;
            bool changed = state != _flowState || snap.Data.HoleIndex != _flowHole || snap.Data.SessionId != _flowSession;
            if (changed && _flowState != (SessionState)(-1) && !_interior) Enter(state, snap);
            _flowState = state;
            _flowHole = snap.Data.HoleIndex;
            _flowSession = snap.Data.SessionId;

            if (state != SessionState.SHOT_SETUP || _interior) return;
            int player = snap.CurrentPlayer?.Id ?? -1;
            string club = snap.Data.Setup.ClubId;
            if (player == _clubPlayer && _clubId != null && club != null && club != _clubId) PlayFlat(GolfSound.ClubChange);
            _clubPlayer = player;
            _clubId = club;
        }

        private void Enter(SessionState state, SessionSnapshot snap)
        {
            switch (state)
            {
                case SessionState.BRIEFING:
                    PlayFlat(GolfSound.UiTitle);
                    break;
                case SessionState.SHOT_ARMED:
                    PlayFlat(GolfSound.UiConfirm, 0.5f);
                    break;
                case SessionState.HOLE_COMPLETE:
                    Schedule(0.2f, () => PlayFlat(GolfSound.UiConfirm));
                    break;
                case SessionState.ROUND_COMPLETE:
                    if (snap.Data.Completion != RoundCompletion.Complete) break;
                    React(0.5f, GolfSound.StingerRound);
                    React(0.8f, GolfSound.Ovation);
                    break;
            }
        }
    }
}
