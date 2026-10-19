using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Sits a Humanoid golfer in the cart after the Animator has posed them (there is no sitting clip): the hips go on
    /// the seat, the legs reach the pedals and the hands the steering wheel as two-bone chains, and the spine leans back
    /// a little. Drives whatever body is shown; does nothing while <see cref="Animator"/> or the targets are unset.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class SeatedDriverPose : MonoBehaviour
    {
        public Transform Root;
        public Animator Animator;
        [Tooltip("The cart: the golfer faces its forward.")]
        public Transform Frame;
        public Transform Seat, FootL, FootR, HandL, HandR;
        [Tooltip("The golfer's bag (a child of Root) and where it rides on the cart.")]
        public Transform Bag, BagMount;
        [Tooltip("Degrees the upper body leans back against the seat.")]
        public float Recline = 9f;

        private Animator _bound;
        private Transform _hips, _spine, _head;
        private Transform _thighL, _shinL, _footL, _thighR, _shinR, _footR;
        private Transform _armL, _elbowL, _handL, _armR, _elbowR, _handR;

        private void LateUpdate()
        {
            if (Root == null || Animator == null || Seat == null || !Animator.isActiveAndEnabled) return;
            if (_bound != Animator) Bind();
            if (_hips == null) return;

            Transform frame = Frame != null ? Frame : Seat;
            Root.rotation = frame.rotation;
            Root.position += Seat.position - _hips.position;
            if (Bag != null && BagMount != null) Bag.SetPositionAndRotation(BagMount.position, Quaternion.LookRotation(-frame.forward, frame.up));

            Vector3 fwd = frame.forward, up = frame.up, right = frame.right;
            if (_spine != null)
            {
                Quaternion head = _head != null ? _head.rotation : Quaternion.identity;
                _spine.rotation = Quaternion.AngleAxis(-Recline, right) * _spine.rotation;
                if (_head != null) _head.rotation = Quaternion.Slerp(_head.rotation, head, 0.6f);
            }

            // Feet keep the standing pose's flat orientation (now in the cart's frame) once the legs are bent.
            Quaternion soleL = _footL != null ? _footL.rotation : Quaternion.identity;
            Quaternion soleR = _footR != null ? _footR.rotation : Quaternion.identity;
            Solve(_thighL, _shinL, _footL, FootL, fwd + up * 0.6f);
            Solve(_thighR, _shinR, _footR, FootR, fwd + up * 0.6f);
            if (_footL != null && FootL != null) _footL.rotation = soleL;
            if (_footR != null && FootR != null) _footR.rotation = soleR;
            Solve(_armL, _elbowL, _handL, HandL, -up - right * 0.7f);
            Solve(_armR, _elbowR, _handR, HandR, -up + right * 0.7f);
        }

        private void Bind()
        {
            _bound = Animator;
            _hips = _spine = _head = null;
            if (!Animator.isHuman) return;
            _hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = Animator.GetBoneTransform(HumanBodyBones.Spine);
            _head = Animator.GetBoneTransform(HumanBodyBones.Head);
            _thighL = Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _shinL = Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _footL = Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _thighR = Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _shinR = Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _footR = Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            _armL = Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _elbowL = Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _handL = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _armR = Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _elbowR = Animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _handR = Animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        /// <summary>Bends a chain so its end reaches <paramref name="target"/>, the middle joint toward <paramref name="pole"/>.</summary>
        private static void Solve(Transform a, Transform b, Transform c, Transform target, Vector3 pole)
        {
            if (a == null || b == null || c == null || target == null) return;
            Vector3 root = a.position;
            float upper = (b.position - root).magnitude, lower = (c.position - b.position).magnitude;
            Vector3 toTarget = target.position - root;
            float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 1e-3f, (upper + lower) * 0.999f);
            if (toTarget.sqrMagnitude < 1e-8f) return;
            Vector3 dir = toTarget.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.up, dir);
            bend.Normalize();
            float cos = Mathf.Clamp((upper * upper + reach * reach - lower * lower) / (2f * upper * reach), -1f, 1f);
            Vector3 joint = root + dir * (upper * cos) + bend * (upper * Mathf.Sqrt(1f - cos * cos));
            a.rotation = Quaternion.FromToRotation(b.position - root, joint - root) * a.rotation;
            Vector3 end = root + dir * reach;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, end - b.position) * b.rotation;
        }
    }
}
