using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Views;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// The hole intro shown under the briefing: from wherever the camera is (the cart drive's reveal, or the first tee) it
    /// rises behind the tee, glides down the line of play, swings round the flag, climbs back over the hole and settles
    /// on the address view behind the ball, where <see cref="GolfCameraRig"/> picks up without a jump. Like
    /// <see cref="CartDrive"/> it takes the camera from the rig while active and hands it back. Purely presentation.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HoleFlyover : MonoBehaviour
    {
        private const float MinSeconds = 8f;
        private const float MaxSeconds = 12f;

        private readonly List<Vector3> _pos = new List<Vector3>();
        private readonly List<Vector3> _look = new List<Vector3>();
        private readonly List<float> _at = new List<float>();
        private HoleDefinition _hole;
        private Camera _camera;
        private GolfCameraRig _rig;
        private float _t, _duration;

        public bool Active { get; private set; }
        /// <summary>True once the camera has settled on the address view (the flyover then holds there).</summary>
        public bool Settled => Active && _t >= _duration;
        public float Duration => _duration;

        public static HoleFlyover Create(Transform parent)
        {
            var go = new GameObject("Hole Flyover");
            go.transform.SetParent(parent, false);
            return go.AddComponent<HoleFlyover>();
        }

        public void Begin(HoleDefinition hole, Camera camera, GolfCameraRig rig)
        {
            if (hole == null || camera == null) return;
            _hole = hole;
            _camera = camera;
            _rig = rig;
            if (_rig != null) _rig.enabled = false;
            BuildPath(hole, camera.transform);
            _t = 0f;
            Active = true;
            Apply();
        }

        /// <summary>Gives the camera back to the rig, which eases on from the current pose.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            _hole = null;
            if (_rig != null) _rig.enabled = true;
        }

        private void LateUpdate()
        {
            if (!Active) return;
            _t = Mathf.Min(_t + UnityEngine.Time.deltaTime, _duration);
            Apply();
        }

        private void Apply()
        {
            if (_camera == null) return;
            float u = Ease(_duration > 0f ? _t / _duration : 1f);
            int seg = 0;
            while (seg < _at.Count - 2 && u > _at[seg + 1]) seg++;
            float span = Mathf.Max(1e-4f, _at[seg + 1] - _at[seg]);
            float f = Mathf.Clamp01((u - _at[seg]) / span);
            Vector3 p = CatmullRom(_pos, seg, f);
            Vector3 look = CatmullRom(_look, seg, f);
            Vector3 forward = look - p;
            if (forward.sqrMagnitude < 1e-4f) return;
            _camera.transform.SetPositionAndRotation(p, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>
        /// Key poses in world space. The last one is <see cref="GolfCameraRig"/>'s address framing for a ball on the tee
        /// aimed at the cup (the setup aim of every first stroke), so the hand-back is continuous.
        /// </summary>
        private void BuildPath(HoleDefinition hole, Transform from)
        {
            _pos.Clear();
            _look.Clear();
            Vector3 tee = hole.TeeM.ToUnity(), cup = hole.CupM.ToUnity();
            Vector3 line = cup - tee;
            line.y = 0f;
            float length = Mathf.Max(20f, line.magnitude);
            Vector3 d = line / length;
            Vector3 side = Vector3.Cross(Vector3.up, d);

            // The play route bends for doglegs; sample it rather than the straight tee-cup line.
            var route = new List<Vector3>();
            foreach (Vec3d p in hole.PlayRouteM) route.Add(p.ToUnity());
            if (route.Count < 2) route = new List<Vector3> { tee, cup };
            Vector3 mid = RoutePoint(route, 0.5f);
            Vector3 approach = RoutePoint(route, 0.78f);
            float lift = Mathf.Clamp(length * 0.12f, 14f, 34f);

            Add(from.position, from.position + from.forward * 50f);
            Add(tee - d * 16f + Vector3.up * 8f, tee + d * 50f + Vector3.up * 2f);
            Add(mid - Heading(route, 0.5f) * 30f + Vector3.up * lift, RoutePoint(route, 0.8f));
            Add(approach - Heading(route, 0.78f) * 12f + Vector3.up * (lift * 0.6f), cup);
            Add(cup + side * 20f - d * 6f + Vector3.up * 8f, cup + Vector3.up * 0.5f);
            Add(cup + d * 16f + side * 6f + Vector3.up * 10f, cup - d * 18f);
            Add(Vector3.Lerp(tee, cup, 0.55f) + d * 10f - side * 22f + Vector3.up * (lift + 24f), Vector3.Lerp(tee, cup, 0.35f));
            Add(tee - d * 7.5f + Vector3.up * 2.6f, tee + d * 40f + Vector3.up * 2f);

            // Time per leg grows sub-linearly with its length: long glides move fast, the flag swing lingers.
            _at.Clear();
            float total = 0f;
            var legs = new float[_pos.Count - 1];
            for (int i = 0; i < legs.Length; i++)
            {
                legs[i] = Mathf.Pow(Mathf.Max(8f, Vector3.Distance(_pos[i], _pos[i + 1])), 0.55f);
                total += legs[i];
            }
            float acc = 0f;
            _at.Add(0f);
            foreach (float leg in legs)
            {
                acc += leg;
                _at.Add(acc / total);
            }
            _duration = Mathf.Clamp(MinSeconds + length / 70f, MinSeconds, MaxSeconds);
        }

        private void Add(Vector3 pos, Vector3 look)
        {
            _pos.Add(pos);
            _look.Add(look);
        }

        private static Vector3 RoutePoint(List<Vector3> route, float f)
        {
            float total = 0f;
            for (int i = 1; i < route.Count; i++) total += Vector3.Distance(route[i - 1], route[i]);
            float goal = total * Mathf.Clamp01(f);
            for (int i = 1; i < route.Count; i++)
            {
                float leg = Vector3.Distance(route[i - 1], route[i]);
                if (goal <= leg || i == route.Count - 1) return Vector3.Lerp(route[i - 1], route[i], leg > 1e-4f ? Mathf.Clamp01(goal / leg) : 1f);
                goal -= leg;
            }
            return route[route.Count - 1];
        }

        private static Vector3 Heading(List<Vector3> route, float f)
        {
            Vector3 a = RoutePoint(route, Mathf.Max(0f, f - 0.05f)), b = RoutePoint(route, Mathf.Min(1f, f + 0.05f));
            Vector3 h = b - a;
            h.y = 0f;
            return h.sqrMagnitude > 1e-4f ? h.normalized : Vector3.forward;
        }

        private static Vector3 CatmullRom(List<Vector3> pts, int seg, float t)
        {
            Vector3 p0 = pts[Mathf.Max(0, seg - 1)], p1 = pts[seg], p2 = pts[seg + 1], p3 = pts[Mathf.Min(pts.Count - 1, seg + 2)];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Ease in and out so the move leaves the start pose and arrives on the address view at rest.</summary>
        private static float Ease(float p)
        {
            p = Mathf.Clamp01(p);
            return p * p * p * (p * (6f * p - 15f) + 10f);
        }
    }
}
