using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Course;
using Puttshack.Session;
using UnityEngine;

namespace Puttshack.Runtime.Audio
{
    /// <summary>
    /// The course bed: a 2D surf wash that thins with distance from the shore, positional surf emitters that slide along
    /// the coastline (<see cref="GrayboxCourse.ShoreControl"/>) around the listener's nearest point, wind that follows the
    /// hole's wind and the camera's height, inland birds, the gallery murmur by the green, gulls off the coast, the
    /// cart's motor and the optional music beds.
    /// </summary>
    public sealed partial class GolfAudioDirector
    {
        private LoopVoice _surfBed, _wind, _birds, _murmur, _cartHum, _musicMenu, _musicRound;
        private LoopVoice[] _shore;
        private bool _shorePlaced;
        private readonly List<Vector3> _shoreline = new List<Vector3>();
        private readonly List<float> _shoreArc = new List<float>();
        private float _shoreDistance = float.MaxValue;
        private float _shoreNearestArc;
        private float _nextGull;
        private CartDrive _cartDrive;
        private Transform _cartBody;
        private float _nextCartSearch;
        private Vector3 _lastCartPos;
        private float _cartSpeed;

        private void BuildLoops()
        {
            _surfBed = Loop(GolfSound.SurfBed, lowPass: true);
            _wind = Loop(GolfSound.Wind, lowPass: true);
            _birds = Loop(GolfSound.Birds, lowPass: true);
            _murmur = Loop(GolfSound.CrowdMurmur, lowPass: true);
            _shore = new LoopVoice[Mathf.Clamp(_bank.Ambience.ShoreEmitters, 1, 5)];
            for (int i = 0; i < _shore.Length; i++)
            {
                _shore[i] = Loop(GolfSound.SurfShore, lowPass: true);
                _shore[i].Source.name = $"SurfShore {i + 1}";
                _shore[i].FadeSeconds = 1.5f;
            }
            foreach (GolfSound roll in new[] { GolfSound.RollGreen, GolfSound.RollFairway, GolfSound.RollCartPath })
                _rollLoops[roll] = Loop(roll, lowPass: false);
            _cartHum = Loop(GolfSound.CartHum, lowPass: false);
            _musicMenu = Loop(GolfSound.MusicMenu, lowPass: false);
            _musicRound = Loop(GolfSound.MusicRound, lowPass: false);
            _musicMenu.FadeSeconds = _musicRound.FadeSeconds = 1.5f;
            _musicMenu.FromStart = _musicRound.FromStart = true;
            BuildShoreline();
            _nextGull = UnityEngine.Time.time + 3f;
        }

        private void BuildShoreline()
        {
            float y = (float)GrayboxCourse.SeaLevelM + _bank.Ambience.ShoreEmitterLift;
            float arc = 0f;
            foreach (Vec2d p in Shapes.CatmullRom(GrayboxCourse.ShoreControl, 4))
            {
                var v = new Vector3((float)p.x, y, (float)p.z);
                if (_shoreline.Count > 0) arc += Vector3.Distance(_shoreline[_shoreline.Count - 1], v);
                _shoreline.Add(v);
                _shoreArc.Add(arc);
            }
        }

        private void UpdateAmbience(SessionSnapshot snap, float dt)
        {
            AmbienceSettings a = _bank.Ambience;
            MixSettings m = _bank.Mix;
            Vector3 listener = ListenerPosition;
            NearestShore(listener);
            HoleDefinition hole = snap.Hole;
            bool outdoors = !_interior;
            // The course is heard from attract to the standings; indoors the beds drop and muffle through the bus.
            bool course = _inRound || snap.EffectiveState == SessionState.ATTRACT || snap.EffectiveState == SessionState.LOBBY || _interior;

            float coast = Mathf.InverseLerp(a.SurfFullDistance, a.SurfFarDistance, _shoreDistance);
            _surfBed.Target = course ? Mathf.Lerp(1f, a.SurfFarLevel, coast) : 0f;
            _birds.Target = course ? Mathf.Lerp(a.BirdsAtCoast, a.BirdsInland, Mathf.InverseLerp(30f, 160f, _shoreDistance)) : 0f;

            float mps = hole != null ? (float)hole.WindMps.HorizontalLength : 3f;
            float height = listener.y - (hole != null ? (float)hole.TeeM.y : 0f);
            float wind = a.WindBase + a.WindPerMps * mps + a.WindHeightBoost * Mathf.Clamp01(height / 60f);
            if (_putting) wind *= DbToLinear(m.PuttWindDb);
            _wind.Target = course ? Mathf.Clamp01(wind) : 0f;

            float murmur = 0f;
            if (_inRound && outdoors && hole != null)
            {
                float toGreen = Vector3.Distance(listener, hole.CupM.ToUnity());
                murmur = Mathf.Lerp(a.MurmurNearGreen, a.MurmurFar, Mathf.InverseLerp(40f, 220f, toGreen));
                if (_putting) murmur *= DbToLinear(m.PuttMurmurDb);
                else if (_address) murmur *= DbToLinear(m.AddressMurmurDb);
            }
            _murmur.Target = murmur;

            PlaceShoreEmitters(dt, course);
            Gulls(outdoors && course);
            Cart(dt);
        }

        /// <summary>Distance (XZ) from the listener to the shoreline and the arc length of the nearest point.</summary>
        private void NearestShore(Vector3 listener)
        {
            _shoreDistance = float.MaxValue;
            var l = new Vector2(listener.x, listener.z);
            for (int i = 0; i + 1 < _shoreline.Count; i++)
            {
                var p = new Vector2(_shoreline[i].x, _shoreline[i].z);
                var q = new Vector2(_shoreline[i + 1].x, _shoreline[i + 1].z);
                Vector2 seg = q - p;
                float f = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(l - p, seg) / seg.sqrMagnitude) : 0f;
                float d = Vector2.Distance(l, p + seg * f);
                if (d >= _shoreDistance) continue;
                _shoreDistance = d;
                _shoreNearestArc = Mathf.Lerp(_shoreArc[i], _shoreArc[i + 1], f);
            }
        }

        private Vector3 ShoreAt(float arc, out Vector3 seaward)
        {
            seaward = Vector3.left;
            if (_shoreline.Count < 2) return Vector3.zero;
            arc = Mathf.Clamp(arc, 0f, _shoreArc[_shoreArc.Count - 1]);
            int i = 0;
            while (i < _shoreArc.Count - 2 && _shoreArc[i + 1] < arc) i++;
            float span = _shoreArc[i + 1] - _shoreArc[i];
            float f = span > 1e-4f ? (arc - _shoreArc[i]) / span : 0f;
            Vector3 tangent = _shoreline[i + 1] - _shoreline[i];
            tangent.y = 0f;
            // Looking along the shore (south to north) the sea is on the left.
            if (tangent.sqrMagnitude > 1e-6f) seaward = Vector3.Cross(tangent.normalized, Vector3.up);
            return Vector3.Lerp(_shoreline[i], _shoreline[i + 1], f);
        }

        private void PlaceShoreEmitters(float dt, bool course)
        {
            AmbienceSettings a = _bank.Ambience;
            float k = _shorePlaced ? Ease(dt, 1.5f) : 1f;
            for (int i = 0; i < _shore.Length; i++)
            {
                float offset = (i - (_shore.Length - 1) * 0.5f) * a.ShoreEmitterSpacing;
                Vector3 goal = ShoreAt(_shoreNearestArc + offset, out _);
                Transform t = _shore[i].Source.transform;
                t.position = Vector3.Lerp(t.position, goal, k);
                _shore[i].Target = course ? 1f : 0f;
            }
            _shorePlaced = true;
        }

        private void Gulls(bool active)
        {
            if (UnityEngine.Time.time < _nextGull) return;
            AmbienceSettings a = _bank.Ambience;
            _nextGull = UnityEngine.Time.time + Random.Range(a.GullInterval.x, Mathf.Max(a.GullInterval.x, a.GullInterval.y));
            if (!active || _shoreDistance > a.GullMaxShoreDistance) return;
            Vector3 at = ShoreAt(_shoreNearestArc + Random.Range(-80f, 80f), out Vector3 seaward) +
                         seaward * Random.Range(5f, 60f) + Vector3.up * Random.Range(8f, 28f);
            PlayCue(GolfSound.Gull, at);
            if (Random.value < 0.35f)
            {
                Vector3 second = at + new Vector3(Random.Range(-12f, 12f), Random.Range(-3f, 3f), Random.Range(-12f, 12f));
                Schedule(Random.Range(0.35f, 0.9f), () => PlayCue(GolfSound.Gull, second, 0.8f));
            }
        }

        private bool CartActive()
        {
            if (_cartDrive == null && UnityEngine.Time.unscaledTime >= _nextCartSearch)
            {
                _nextCartSearch = UnityEngine.Time.unscaledTime + 2f;
                _cartDrive = FindAnyObjectByType<CartDrive>();
                _cartBody = _cartDrive != null ? _cartDrive.transform.Find("Golf Cart") : null;
                if (_cartBody != null) _lastCartPos = _cartBody.position;
            }
            return _cartDrive != null && _cartDrive.Active;
        }

        private void Cart(float dt)
        {
            bool driving = CartActive() && _cartBody != null;
            if (_cartBody != null)
            {
                Vector3 pos = _cartBody.position;
                float step = UnityEngine.Time.deltaTime > 1e-5f ? Vector3.Distance(pos, _lastCartPos) / UnityEngine.Time.deltaTime : 0f;
                _lastCartPos = pos;
                _cartSpeed += (step - _cartSpeed) * Ease(dt, 0.25f);
                _cartHum.Source.transform.position = pos + Vector3.up * 0.5f;
            }
            float k = Mathf.Clamp01(_cartSpeed / 10f);
            _cartHum.Target = driving ? 0.25f + 0.75f * k : 0f;
            _cartHum.Pitch = Mathf.Lerp(0.8f, 1.25f, k);
        }

        private void UpdateMusic()
        {
            bool round = _inRound && !_interior;
            float duck = _putting ? 0.5f : 1f;
            _musicRound.Target = round ? duck : 0f;
            _musicMenu.Target = round ? 0f : 1f;
        }
    }
}
