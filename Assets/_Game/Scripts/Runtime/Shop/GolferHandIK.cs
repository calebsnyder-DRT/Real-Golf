using System;
using System.Collections.Generic;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Keeps both hands on the club after the Animator has posed the body. The club rides the lead hand on its per-club
    /// seat (<c>ClubGrip_&lt;Model&gt;</c> under ClubGrip.L, +Y down the shaft), so the right arm is solved as a two-bone chain,
    /// the shoulder turning in where the arm alone falls short, onto a wrist pose held in that seat's space, just below the
    /// lead hand on the grip (measured at bake time by
    /// GolferCharacterBaker). Following the club rather than the clip keeps both hands on the grip through the whole swing,
    /// finish included, on every body the Humanoid clips are retargeted to. Around impact the lead hand eases over by
    /// however far the clip's clubface passes from where it addressed the ball, so the face meets the ball.
    /// <see cref="GolferMotion"/> holds the grip from address to the end of the swing and lets go for idles and reactions;
    /// the hands blend on and off.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class GolferHandIK : MonoBehaviour
    {
        [Serializable]
        public struct Grip
        {
            [Tooltip("Club model: Driver, Iron, Wedge, Putter.")]
            public string Model;
            [Tooltip("The club's seat in the lead hand.")]
            public Transform Seat;
            [Tooltip("Right wrist position in seat space with both hands on the grip.")]
            public Vector3 Offset;
            [Tooltip("Right wrist rotation in seat space.")]
            public Quaternion Rotation;
            [Tooltip("Swing state the strike happens in.")]
            public string Swing;
            [Tooltip("Seconds from the start of Swing to impact (0: no strike).")]
            public float Contact;
            [Tooltip("How far the left wrist moves at impact for the clubface to meet the ball, in the Animator's space.")]
            public Vector3 LeadShift;
        }

        public List<Grip> Grips = new List<Grip>();
        [Tooltip("Seconds to blend the hand onto or off the grip.")]
        public float BlendTime = 0.2f;
        [Tooltip("Seconds either side of impact over which the lead hand eases over onto the ball.")]
        public float StrikeWindow = 0.12f;
        [Tooltip("Most the right shoulder turns toward the grip when the arm alone can't reach it, in degrees.")]
        public float ShoulderReach = 30f;

        private Animator _animator;
        private Transform _shoulder, _upper, _lower, _hand;
        private Transform _leadUpper, _leadLower, _leadHand;
        private int _grip = -1;
        private int _blend = -1;
        private float _w;

        /// <summary>Club model the right hand is holding (null when it has let go).</summary>
        public string Held => _grip >= 0 ? Grips[_grip].Model : null;

        public float Weight => _w;

        public void Hold(string model)
        {
            _grip = Grips.FindIndex(g => g.Model == model && g.Seat != null);
            if (_grip >= 0) _blend = _grip;
        }

        public void Release() => _grip = -1;

        /// <summary>Jumps straight to the held or released weight (posing outside play mode).</summary>
        public void Snap() => _w = _grip >= 0 ? 1f : 0f;

        private void Awake() => Bind();

        private void Bind()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null || !_animator.isHuman) return;
            _shoulder = _animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            _upper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lower = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _leadUpper = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leadLower = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leadHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }

        private void LateUpdate()
        {
            float rate = BlendTime > 0f ? UnityEngine.Time.deltaTime / BlendTime : 1f;
            _w = Mathf.MoveTowards(_w, _grip >= 0 ? 1f : 0f, rate);
            Apply();
        }

        /// <summary>Solves the arms onto the club at the current weight (editor tools call it after sampling a pose).</summary>
        public void Apply()
        {
            if (_hand == null) Bind();
            if (_w <= 0.001f || _blend < 0 || _blend >= Grips.Count || _hand == null || _upper == null || _lower == null) return;
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            Grip g = Grips[_blend];
            if (g.Seat == null) return;
            float strike = Strike(g) * _w;
            if (strike > 0.001f && _leadHand != null && _leadUpper != null && _leadLower != null)
            {
                Quaternion turn = _leadHand.rotation;
                Solve(_leadUpper, _leadLower, _leadHand, _leadHand.position + _animator.transform.TransformVector(g.LeadShift), strike);
                _leadHand.rotation = turn;
            }
            Vector3 target = g.Seat.TransformPoint(g.Offset);
            if (_shoulder != null) Reach(target, _w);
            Solve(_upper, _lower, _hand, target, _w);
            _hand.rotation = Quaternion.Slerp(_hand.rotation, g.Seat.rotation * g.Rotation, _w);
        }

        /// <summary>
        /// Turns the right shoulder toward the target by just enough for the arm to reach it (the trail shoulder drops and
        /// comes forward to the grip), at most <see cref="ShoulderReach"/> degrees.
        /// </summary>
        private void Reach(Vector3 target, float weight)
        {
            float arm = ((_lower.position - _upper.position).magnitude + (_hand.position - _lower.position).magnitude) * 0.98f;
            Vector3 s = _shoulder.position, u = _upper.position;
            if ((target - u).magnitude <= arm) return;
            Quaternion full = Quaternion.FromToRotation(u - s, target - s);
            float angle = Quaternion.Angle(Quaternion.identity, full);
            if (angle < 0.01f) return;
            float lo = 0f, hi = Mathf.Min(1f, ShoulderReach / angle);
            if ((target - (s + Quaternion.Slerp(Quaternion.identity, full, hi) * (u - s))).magnitude <= arm)
            {
                for (int i = 0; i < 10; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if ((target - (s + Quaternion.Slerp(Quaternion.identity, full, mid) * (u - s))).magnitude > arm) lo = mid;
                    else hi = mid;
                }
            }
            _shoulder.rotation = Quaternion.Slerp(Quaternion.identity, full, hi * weight) * _shoulder.rotation;
        }

        /// <summary>1 at impact in the grip's swing, easing to 0 a <see cref="StrikeWindow"/> either side and outside it.</summary>
        private float Strike(Grip g)
        {
            if (g.Contact <= 0f || StrikeWindow <= 0f || string.IsNullOrEmpty(g.Swing)) return 0f;
            int hash = Animator.StringToHash(g.Swing);
            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash != hash)
            {
                if (!_animator.IsInTransition(0)) return 0f;
                info = _animator.GetNextAnimatorStateInfo(0);
                if (info.shortNameHash != hash) return 0f;
            }
            float x = Mathf.Abs(info.normalizedTime * info.length - g.Contact) / StrikeWindow;
            return x >= 1f ? 0f : 1f - x * x * (3f - 2f * x);
        }

        /// <summary>Two-bone solve of upper-lower-hand toward the target that keeps the animated elbow direction as the pole.</summary>
        private static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, float weight)
        {
            Vector3 a = upper.position, b = lower.position, c = hand.position;
            float ab = (b - a).magnitude, bc = (c - b).magnitude;
            Vector3 goal = Vector3.Lerp(c, target, weight);
            Vector3 toGoal = goal - a;
            float reach = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(ab - bc) + 1e-4f, (ab + bc) * 0.9995f);
            Vector3 dir = toGoal.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(b - a, dir);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(upper.up, dir);
            pole.Normalize();
            float cosA = Mathf.Clamp((ab * ab + reach * reach - bc * bc) / (2f * ab * reach), -1f, 1f);
            Vector3 elbow = a + dir * (ab * cosA) + pole * (ab * Mathf.Sqrt(1f - cosA * cosA));

            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            Vector3 handNow = hand.position;
            lower.rotation = Quaternion.FromToRotation(handNow - lower.position, a + dir * reach - lower.position) * lower.rotation;
        }
    }
}
