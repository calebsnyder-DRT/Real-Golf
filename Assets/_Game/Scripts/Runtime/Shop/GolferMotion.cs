using System;
using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Plays the golfer's clips on whichever body <see cref="GolferAppearance"/> shows: idle (shop and waiting), address
    /// with the selected club, the swing (whose impact time the course presentation waits for before launching the ball),
    /// and the reaction to the result. Also blinks and sets the face expression on heads that have the shapes. Impact
    /// times and ball placement are written at bake time (<c>Puttshack > Characters > Bake Golfers</c>).
    /// </summary>
    public sealed class GolferMotion : MonoBehaviour
    {
        [Serializable]
        public struct ClubMotion
        {
            [Tooltip("Club model: Driver, Iron, Wedge, Putter.")]
            public string Model;
            public string Address;
            public string Swing;
            [Tooltip("Seconds from the start of Swing to impact.")]
            public float Contact;
            [Tooltip("Ball centre in golfer-root space at address, per character (index = character).")]
            public Vector3[] Ball;
        }

        public enum Reaction
        {
            None,
            Good,
            Miss,
            Holed,
            [Tooltip("Water or out of bounds.")]
            Penalty,
        }

        public GolferAppearance Appearance;
        public List<ClubMotion> Clubs = new List<ClubMotion>();
        public string IdleState = "Idle";
        public string GoodState = "React_Good";
        public string MissState = "React_Miss";
        public string HoledState = "Celebrate";
        public float Fade = 0.2f;
        [Tooltip("Resting smile on the face (0-1); kept small so the face stays natural.")]
        public float RestSmile = 0.08f;

        private string _state;
        private int _stateBody = int.MinValue;
        private string _club;
        private float _nextBlink;
        private float _blinkT = -1f;
        private float _smile, _frown, _open;
        private float _smileGoal, _frownGoal, _openGoal;
        private float _expressionUntil;

        public string State => _state;

        public static string ClubModel(string clubId)
        {
            switch (clubId)
            {
                case "driver":
                case "wood3":
                    return "Driver";
                case "pw":
                case "sw":
                case "lw":
                    return "Wedge";
                case ClubCatalog.PutterId:
                    return "Putter";
                default:
                    return "Iron";
            }
        }

        public bool TryGet(string model, out ClubMotion motion)
        {
            foreach (ClubMotion c in Clubs)
            {
                if (c.Model == model)
                {
                    motion = c;
                    return true;
                }
            }
            motion = default;
            return false;
        }

        /// <summary>Ball centre in golfer-root space at address for the shown body, if measured.</summary>
        public bool BallOffset(string clubId, out Vector3 offset)
        {
            offset = default;
            if (!TryGet(ClubModel(clubId), out ClubMotion m) || m.Ball == null || m.Ball.Length == 0) return false;
            int body = Appearance != null ? Mathf.Max(0, Appearance.Shown) : 0;
            offset = m.Ball[Mathf.Min(body, m.Ball.Length - 1)];
            return true;
        }

        public void Idle()
        {
            _club = null;
            if (Appearance != null) Appearance.Hold(null);
            Play(IdleState, Fade);
        }

        /// <summary>Club put away, idling by the ball until it is this golfer's turn.</summary>
        public void Wait() => Idle();

        public void Address(string clubId)
        {
            string model = ClubModel(clubId);
            _club = model;
            if (Appearance != null && Appearance.HeldClub != model) Appearance.Hold(model);
            TryGet(model, out ClubMotion m);
            Play(m.Address, Fade);
        }

        /// <summary>Starts the swing for the club; returns seconds until the clubhead meets the ball (0 if there is no swing).</summary>
        public float Swing(string clubId)
        {
            string model = ClubModel(clubId);
            if (_club != model) Address(clubId);
            if (!TryGet(model, out ClubMotion m)) return 0f;
            return Play(m.Swing, 0.06f) ? m.Contact : 0f;
        }

        public void React(Reaction reaction)
        {
            switch (reaction)
            {
                case Reaction.Holed:
                    Play(HoledState, 0.25f);
                    Express(smile: 0.9f, frown: 0f, open: 0.35f, seconds: 3.5f);
                    break;
                case Reaction.Good:
                    Play(GoodState, 0.3f);
                    Express(smile: 0.55f, frown: 0f, open: 0.1f, seconds: 2.5f);
                    break;
                case Reaction.Miss:
                case Reaction.Penalty:
                    Play(MissState, 0.3f);
                    Express(smile: 0f, frown: 0.7f, open: 0.1f, seconds: 2.5f);
                    break;
            }
        }

        /// <summary>How the golfer reacts to a finished shot.</summary>
        public static Reaction ReactionTo(ShotRecord shot, Vector3 cup)
        {
            ShotResult r = shot?.Result;
            if (r == null) return Reaction.None;
            switch (r.Outcome)
            {
                case ShotOutcome.Holed:
                    return Reaction.Holed;
                case ShotOutcome.Water:
                case ShotOutcome.OutOfBounds:
                    return Reaction.Penalty;
                case ShotOutcome.SolverFault:
                    return Reaction.None;
            }
            Vector3 end = r.FinalPosition.ToUnity();
            float left = Vector2.Distance(new Vector2(end.x, end.z), new Vector2(cup.x, cup.z));
            bool putt = shot.Intent != null && shot.Intent.ClubId == ClubCatalog.PutterId;
            if (putt) return left < 0.9f ? Reaction.Good : left > 3f ? Reaction.Miss : Reaction.None;
            if (r.FinalLie == SurfaceId.Green || left < 4f) return Reaction.Good;
            if (r.FinalLie == SurfaceId.Bunker || r.FinalLie == SurfaceId.DeepRough) return Reaction.Miss;
            return r.FinalLie == SurfaceId.Fairway ? Reaction.Good : Reaction.None;
        }

        /// <summary>Re-issues the current state after the shown body changed (each body has its own Animator).</summary>
        public void Refresh()
        {
            if (_state == null) return;
            string state = _state;
            _state = null;
            Play(state, 0f);
        }

        private bool Play(string state, float fade)
        {
            if (Appearance == null || string.IsNullOrEmpty(state)) return false;
            Animator animator = Appearance.ShownAnimator;
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isActiveAndEnabled) return false;
            int hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash)) return false;
            if (_state != state || _stateBody != Appearance.Shown)
            {
                if (fade > 0f && _stateBody == Appearance.Shown) animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
                else animator.Play(hash, 0, 0f);
            }
            _state = state;
            _stateBody = Appearance.Shown;
            return true;
        }

        private void Express(float smile, float frown, float open, float seconds)
        {
            _smileGoal = smile;
            _frownGoal = frown;
            _openGoal = open;
            _expressionUntil = UnityEngine.Time.time + seconds;
        }

        private void OnEnable()
        {
            _nextBlink = UnityEngine.Time.time + UnityEngine.Random.Range(1f, 3f);
            _state = null;
        }

        private void LateUpdate()
        {
            if (Appearance == null) return;
            if (UnityEngine.Time.time > _expressionUntil)
            {
                _smileGoal = RestSmile;
                _frownGoal = 0f;
                _openGoal = 0f;
            }
            float k = 1f - Mathf.Exp(-UnityEngine.Time.deltaTime * 6f);
            _smile = Mathf.Lerp(_smile, _smileGoal, k);
            _frown = Mathf.Lerp(_frown, _frownGoal, k);
            _open = Mathf.Lerp(_open, _openGoal, k);

            float blink = 0f;
            if (_blinkT < 0f && UnityEngine.Time.time >= _nextBlink) _blinkT = 0f;
            if (_blinkT >= 0f)
            {
                _blinkT += UnityEngine.Time.deltaTime;
                const float close = 0.06f, hold = 0.035f, open = 0.09f;
                blink = _blinkT < close ? _blinkT / close : _blinkT < close + hold ? 1f : 1f - (_blinkT - close - hold) / open;
                if (_blinkT >= close + hold + open)
                {
                    _blinkT = -1f;
                    blink = 0f;
                    // Occasional double blink, otherwise every 2-5.5 s.
                    _nextBlink = UnityEngine.Time.time + (UnityEngine.Random.value < 0.15f ? 0.18f : UnityEngine.Random.Range(2f, 5.5f));
                }
            }

            if (!(Appearance.ShownPart("Head_" + Appearance.ShownName) is SkinnedMeshRenderer head) || head.sharedMesh == null) return;
            Mesh mesh = head.sharedMesh;
            SetShape(head, mesh, "Blink_L", blink);
            SetShape(head, mesh, "Blink_R", blink);
            SetShape(head, mesh, "Smile", _smile);
            SetShape(head, mesh, "Frown", _frown);
            SetShape(head, mesh, "Open", _open);
        }

        private static void SetShape(SkinnedMeshRenderer smr, Mesh mesh, string shape, float weight01)
        {
            int i = mesh.GetBlendShapeIndex(shape);
            if (i >= 0) smr.SetBlendShapeWeight(i, Mathf.Clamp01(weight01) * 100f);
        }
    }
}
