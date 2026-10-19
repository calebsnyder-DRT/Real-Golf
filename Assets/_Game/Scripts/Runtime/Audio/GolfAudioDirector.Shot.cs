using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Views;
using Puttshack.Session;
using Puttshack.Simulation;
using UnityEngine;

namespace Puttshack.Runtime.Audio
{
    /// <summary>
    /// Shot sounds, driven by the committed result: the swing whoosh ahead of contact, the strike when the ball leaves,
    /// then each solver event (bounce, roll, tree, lip, cup, water) as the ball's playback time passes it. Impact levels
    /// come from the ball's speed into the ground, read off the trajectory samples.
    /// </summary>
    public sealed partial class GolfAudioDirector
    {
        private const float RollFadeSeconds = 0.08f;

        private ShotRecord _shot;
        private ShotResult _result;
        private BallView _ball;
        private int _trackedShotId = -1;
        private int _searchedShotId = -1;
        private int _eventCursor;
        private bool _shotActive, _shotIsPutt, _struck, _whooshed, _cupPlayed, _lipped, _rolling;
        private float _whooshAt = -1f;
        private double _cupEntryT = -1;
        private SurfaceId _rollSurface;
        private Vector3 _lastBallPos;
        private float _ballSpeed;
        private AnalyticWorld _world;
        private string _worldHoleId;
        private readonly Dictionary<GolfSound, LoopVoice> _rollLoops = new Dictionary<GolfSound, LoopVoice>();

        private void OnShotPlaybackStarted(ShotRecord shot, BallView ball, float launchDelay)
        {
            if (_bank == null || shot?.Result == null || ball == null) return;
            BeginShot(shot, ball);
            if (launchDelay <= 0f || _shotIsPutt) return;
            float speed = _host != null ? Mathf.Max(_host.PlaybackSpeed, 0.01f) : 1f;
            _whooshAt = UnityEngine.Time.time + Mathf.Max(0f, launchDelay / speed - _bank.Impacts.WhooshLead);
        }

        private void BeginShot(ShotRecord shot, BallView ball)
        {
            EndShot(react: false);
            _shot = shot;
            _result = shot.Result;
            _ball = ball;
            _trackedShotId = shot.ShotId;
            _eventCursor = 0;
            _shotActive = true;
            _shotIsPutt = shot.Intent != null && shot.Intent.ClubId == ClubCatalog.PutterId;
            _struck = _whooshed = _cupPlayed = _lipped = _rolling = false;
            _whooshAt = -1f;
            _cupEntryT = _result.Outcome == ShotOutcome.Holed ? CupEntryTime(_result) : -1;
            _lastBallPos = ball.transform.position;
            _ballSpeed = 0f;
        }

        private void EndShot(bool react)
        {
            if (_shotActive && react && _shot != null) ReactToShot(_shot, _lipped);
            _shotActive = false;
            _rolling = false;
            _ball = null;
        }

        private void UpdateShot(SessionSnapshot snap, float dt)
        {
            DiscoverShot(snap);
            if (_shotActive && _ball == null) EndShot(react: false);
            if (!_shotActive)
            {
                UpdateRoll();
                return;
            }

            if (!_whooshed && _whooshAt >= 0f && UnityEngine.Time.time >= _whooshAt) Whoosh();

            float t = _ball.PlaybackTime;
            bool playing = _ball.IsPlaying;
            if (!_struck)
            {
                if (playing && t <= 0f) return;
                if (!playing)
                {
                    // Skipped before contact: only the outcome is heard.
                    Terminal();
                    EndShot(react: true);
                    UpdateRoll();
                    return;
                }
                if (!_whooshed && !_shotIsPutt) Whoosh();
                Strike();
                _struck = true;
            }

            TrackBallSpeed(dt);
            if (playing)
            {
                Fire(t);
            }
            else
            {
                if (t < _result.DurationS - 0.05) Terminal();
                else Fire(double.MaxValue);
                EndShot(react: true);
            }
            UpdateRoll();
        }

        /// <summary>Without the GameHost hook the shot is still heard: the only ball playing back is the shooter's.</summary>
        private void DiscoverShot(SessionSnapshot snap)
        {
            SessionState state = snap.EffectiveState;
            if (state != SessionState.SHOT_PRESENTING && state != SessionState.SHOT_RESOLVED) return;
            ShotRecord last = snap.LastShot;
            if (last?.Result == null || last.ShotId == _trackedShotId || last.ShotId == _searchedShotId) return;
            _searchedShotId = last.ShotId;
            foreach (BallView b in FindObjectsByType<BallView>(FindObjectsInactive.Exclude))
            {
                if (!b.IsPlaying) continue;
                BeginShot(last, b);
                return;
            }
        }

        private void Fire(double upTo)
        {
            if (_cupEntryT >= 0 && !_cupPlayed && upTo >= _cupEntryT)
            {
                _cupPlayed = true;
                PlayCue(GolfSound.CupDrop, _result.FinalPosition.ToUnity());
            }
            List<ShotEvent> events = _result.Events;
            while (_eventCursor < events.Count && events[_eventCursor].t <= upTo)
            {
                Handle(events, _eventCursor);
                _eventCursor++;
            }
        }

        private void Handle(List<ShotEvent> events, int i)
        {
            ShotEvent e = events[i];
            Vector3 at = e.p.ToUnity();
            switch (e.Type)
            {
                case ShotEventType.FirstContact:
                    // The bounce, roll or hazard logged at the same instant voices this contact.
                    if (i + 1 < events.Count && events[i + 1].t - e.t < 1e-4) break;
                    Land(e, at);
                    break;
                case ShotEventType.Bounce:
                    _rolling = false;
                    Land(e, at);
                    break;
                case ShotEventType.StartRoll:
                    if (e.t > 1e-6) Land(e, at);
                    _rolling = true;
                    _rollSurface = e.Surface;
                    break;
                case ShotEventType.TreeHit:
                    PlayCue(GolfSound.TreeHit, at, LandLevel(SpeedBefore(e.t)));
                    break;
                case ShotEventType.LipOut:
                    _lipped = true;
                    PlayCue(GolfSound.LipOut, at);
                    Schedule(0.3f, () => PlayFlat(GolfSound.CrowdOoh));
                    break;
                case ShotEventType.WaterEntry:
                    _rolling = false;
                    PlayCue(GolfSound.Splash, at);
                    break;
                case ShotEventType.Cup:
                    _rolling = false;
                    if (!_cupPlayed)
                    {
                        _cupPlayed = true;
                        PlayCue(GolfSound.CupDrop, at);
                    }
                    break;
                case ShotEventType.OutOfBounds:
                case ShotEventType.Rest:
                case ShotEventType.Fault:
                    _rolling = false;
                    break;
            }
        }

        /// <summary>A skipped presentation jumps to the result: play only what the outcome itself sounds like.</summary>
        private void Terminal()
        {
            _rolling = false;
            Vector3 at = _result.FinalPosition.ToUnity();
            if (_result.Outcome == ShotOutcome.Holed && !_cupPlayed)
            {
                _cupPlayed = true;
                PlayCue(GolfSound.CupDrop, at);
            }
            else if (_result.Outcome == ShotOutcome.Water) PlayCue(GolfSound.Splash, at);
        }

        private void Strike()
        {
            ShotIntent intent = _shot.Intent;
            if (intent == null) return;
            Vector3 at = intent.OriginM.ToUnity();
            float power = Mathf.Clamp01((float)intent.Power01);
            float level = Mathf.Lerp(1f - _bank.Impacts.StrikePowerRange, 1f, power);
            PlayCue(StrikeCue(intent.ClubId), at, level, Mathf.Lerp(0.98f, 1.03f, power));
            if (_shotIsPutt) return;
            if (intent.Lie == SurfaceId.Bunker) PlayCue(GolfSound.StrikeSand, at, Mathf.Lerp(0.7f, 1f, power));
            else if (intent.Lie == SurfaceId.LightRough) PlayCue(GolfSound.StrikeRough, at, 0.75f);
            else if (intent.Lie == SurfaceId.DeepRough) PlayCue(GolfSound.StrikeRough, at, 1f);
        }

        private void Whoosh()
        {
            _whooshed = true;
            ShotIntent intent = _shot?.Intent;
            if (intent == null) return;
            float power = Mathf.Clamp01((float)intent.Power01);
            float pitch = intent.ClubId == "driver" || intent.ClubId == "wood3" ? 0.9f : StrikeCue(intent.ClubId) == GolfSound.StrikeWedge ? 1.1f : 1f;
            PlayCue(GolfSound.Whoosh, intent.OriginM.ToUnity() + Vector3.up, Mathf.Lerp(0.5f, 1f, power), pitch);
        }

        private void Land(ShotEvent e, Vector3 at)
        {
            float speed = SpeedBefore(e.t);
            if (speed < _bank.Impacts.LandSpeedMin) return;
            GolfSound cue = LandCue(e.Surface, out float trim);
            float k = Mathf.InverseLerp(_bank.Impacts.LandSpeedMin, _bank.Impacts.LandSpeedForFull, speed);
            PlayCue(cue, at, LandLevel(speed) * trim, Mathf.Lerp(0.94f, 1.06f, k));
        }

        private float LandLevel(float speed)
        {
            ImpactSettings s = _bank.Impacts;
            return Mathf.Lerp(s.LandQuietestLevel, 1f, Mathf.InverseLerp(s.LandSpeedMin, s.LandSpeedForFull, speed));
        }

        /// <summary>Ball speed (m/s) over the last sampled stretch before <paramref name="t"/>.</summary>
        private float SpeedBefore(double t)
        {
            List<TrajectorySample> s = _result.Samples;
            if (s.Count < 2) return 0f;
            int j = 1;
            for (int k = 1; k < s.Count && s[k].t < t - 1e-6; k++) j = k;
            double dt = s[j].t - s[j - 1].t;
            return dt > 1e-6 ? (float)((s[j].p - s[j - 1].p).Length / dt) : 0f;
        }

        /// <summary>When a holed ball starts its drop: the first sample from which it stays over the cup.</summary>
        private static double CupEntryTime(ShotResult r)
        {
            List<TrajectorySample> s = r.Samples;
            double entry = r.DurationS;
            for (int i = s.Count - 1; i >= 0; i--)
            {
                if (Vec3d.HorizontalDistance(s[i].p, r.FinalPosition) > 0.06) break;
                entry = s[i].t;
            }
            return entry;
        }

        private void TrackBallSpeed(float dt)
        {
            Vector3 pos = _ball.transform.position;
            float step = UnityEngine.Time.deltaTime > 1e-5f ? Vector3.Distance(pos, _lastBallPos) / UnityEngine.Time.deltaTime : 0f;
            _lastBallPos = pos;
            _ballSpeed += (step - _ballSpeed) * Ease(dt, 0.08f);
        }

        private void UpdateRoll()
        {
            ImpactSettings s = _bank.Impacts;
            GolfSound active = (GolfSound)(-1);
            float trim = 0f;
            if (_shotActive && _rolling && _ball != null) active = RollCue(SurfaceUnderBall() ?? _rollSurface, out trim);
            float k = Mathf.Clamp01(_ballSpeed / Mathf.Max(s.RollSpeedForFull, 0.01f));
            foreach (KeyValuePair<GolfSound, LoopVoice> roll in _rollLoops)
            {
                LoopVoice v = roll.Value;
                v.FadeSeconds = RollFadeSeconds;
                v.Target = roll.Key == active ? Mathf.Pow(k, 0.7f) * trim : 0f;
                v.Pitch = Mathf.Lerp(s.RollPitchMin, s.RollPitchMax, k);
                if (_ball != null) v.Source.transform.position = _ball.transform.position;
            }
        }

        private SurfaceId? SurfaceUnderBall()
        {
            if (_host == null || _shot == null) return null;
            CourseDefinition course = _host.Coordinator.Snapshot.Course;
            if (_shot.HoleIndex < 0 || _shot.HoleIndex >= course.Holes.Count) return null;
            HoleDefinition hole = course.Holes[_shot.HoleIndex];
            if (_world == null || _worldHoleId != hole.Id)
            {
                _world = new AnalyticWorld(hole);
                _worldHoleId = hole.Id;
            }
            Vector3 p = _ball.transform.position;
            return _world.SurfaceAt(p.x, p.z);
        }

        private static GolfSound StrikeCue(string clubId)
        {
            switch (clubId)
            {
                case "driver": return GolfSound.StrikeDriver;
                case "wood3": return GolfSound.StrikeWood;
                case "pw":
                case "sw":
                case "lw": return GolfSound.StrikeWedge;
                case ClubCatalog.PutterId: return GolfSound.StrikePutter;
                default: return GolfSound.StrikeIron;
            }
        }

        private static GolfSound LandCue(SurfaceId surface, out float trim)
        {
            trim = 1f;
            switch (surface)
            {
                case SurfaceId.LightRough: return GolfSound.LandRough;
                case SurfaceId.DeepRough:
                    trim = 0.8f;
                    return GolfSound.LandRough;
                case SurfaceId.Bunker: return GolfSound.LandBunker;
                case SurfaceId.Green: return GolfSound.LandGreen;
                case SurfaceId.CartPath: return GolfSound.LandCartPath;
                case SurfaceId.Water: return GolfSound.Splash;
                default: return GolfSound.LandFairway;
            }
        }

        private static GolfSound RollCue(SurfaceId surface, out float trim)
        {
            trim = 1f;
            switch (surface)
            {
                case SurfaceId.Green: return GolfSound.RollGreen;
                case SurfaceId.CartPath: return GolfSound.RollCartPath;
                case SurfaceId.LightRough:
                    trim = 0.6f;
                    return GolfSound.RollFairway;
                case SurfaceId.DeepRough:
                    trim = 0.35f;
                    return GolfSound.RollFairway;
                case SurfaceId.Bunker:
                case SurfaceId.Water:
                    trim = 0f;
                    return GolfSound.RollFairway;
                default: return GolfSound.RollFairway;
            }
        }
    }
}
