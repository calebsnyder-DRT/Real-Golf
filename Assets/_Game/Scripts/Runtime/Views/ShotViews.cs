using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Runtime.Course;
using Puttshack.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Puttshack.Runtime.Views
{
    /// <summary>Animates a committed trajectory. Presentation never alters the stored outcome.</summary>
    public sealed class BallView : MonoBehaviour
    {
        /// <summary>Visual-only enlargement so the ball stays readable on a cabinet display (64 mm vs. the real 42.67 mm).</summary>
        public const float VisualScale = 1.5f;
        private static readonly float BallRadius = (float)SolverParameters.Default.BallRadius;

        private ShotResult _playing;
        private float _time;
        private int _cursor;
        private TrailRenderer _trail;

        public bool IsPlaying => _playing != null;
        public float PlaybackTime => _time;

        public static BallView Create(Transform parent, Material material, Material trailMaterial)
        {
            GameObject go = CourseVisualBuilder.Primitive(PrimitiveType.Sphere, "Ball", parent, material, true);
            go.transform.localScale = Vector3.one * BallRadius * 2 * VisualScale;
            var view = go.AddComponent<BallView>();
            view._trail = go.AddComponent<TrailRenderer>();
            view._trail.time = 1.2f;
            view._trail.minVertexDistance = 0.3f;
            view._trail.widthMultiplier = 0.08f;
            view._trail.material = trailMaterial;
            view._trail.startColor = Palette.Cyan.WithAlpha(0.8f);
            view._trail.endColor = Palette.Cyan.WithAlpha(0f);
            view._trail.shadowCastingMode = ShadowCastingMode.Off;
            view._trail.emitting = false;
            return view;
        }

        public void PlaceAt(Vec3d basePosition)
        {
            _playing = null;
            _trail.emitting = false;
            _trail.Clear();
            transform.position = Lift(basePosition.ToUnity());
        }

        public void Stop()
        {
            _playing = null;
            _trail.emitting = false;
        }

        public void Play(ShotResult result)
        {
            _playing = result;
            _time = 0;
            _cursor = 0;
            _trail.Clear();
            _trail.emitting = true;
            transform.position = Lift(result.Samples[0].p.ToUnity());
        }

        /// <summary>Jumps to the stored final state; no faster physics step is run.</summary>
        public void Skip()
        {
            if (_playing == null) return;
            ShotResult r = _playing;
            PlaceAt(r.FinalPosition);
            if (r.Outcome == ShotOutcome.Holed) transform.position += Vector3.down * 0.06f;
        }

        /// <summary>Returns true when playback has reached the final sample.</summary>
        public bool Advance(float dt)
        {
            if (_playing == null) return true;
            _time += dt;
            List<TrajectorySample> s = _playing.Samples;
            while (_cursor < s.Count - 2 && s[_cursor + 1].t <= _time) _cursor++;
            TrajectorySample a = s[_cursor];
            TrajectorySample b = s[Mathf.Min(_cursor + 1, s.Count - 1)];
            double span = b.t - a.t;
            float f = span > 1e-9 ? Mathf.Clamp01((float)((_time - a.t) / span)) : 1f;
            transform.position = Lift(Vector3.Lerp(a.p.ToUnity(), b.p.ToUnity(), f));

            if (_time >= _playing.DurationS)
            {
                bool holed = _playing.Outcome == ShotOutcome.Holed;
                _trail.emitting = false;
                _playing = null;
                if (holed) transform.position += Vector3.down * 0.06f;
                return true;
            }
            return false;
        }

        private static Vector3 Lift(Vector3 basePosition) => basePosition + Vector3.up * BallRadius * VisualScale;
    }

    /// <summary>Stand-in golfer. Repositioned cosmetically beside the ball; never moves the authoritative ball.</summary>
    public sealed class GolferView : MonoBehaviour
    {
        private Renderer _body;
        private Renderer _cap;

        public static GolferView Create(Transform parent, Material body, Material cap)
        {
            var root = new GameObject("Golfer");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<GolferView>();
            GameObject torso = CourseVisualBuilder.Primitive(PrimitiveType.Capsule, "Body", root.transform, body, true);
            torso.transform.localScale = new Vector3(0.45f, 0.85f, 0.35f);
            torso.transform.localPosition = new Vector3(0, 0.85f, 0);
            GameObject head = CourseVisualBuilder.Primitive(PrimitiveType.Sphere, "Cap", root.transform, cap, true);
            head.transform.localScale = new Vector3(0.26f, 0.2f, 0.26f);
            head.transform.localPosition = new Vector3(0, 1.78f, 0);
            view._body = torso.GetComponent<Renderer>();
            view._cap = head.GetComponent<Renderer>();
            return view;
        }

        public void Stand(Vector3 ballPosition, Vector3 aimForward, Color identity)
        {
            Vector3 fwd = new Vector3(aimForward.x, 0, aimForward.z).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            Vector3 left = Vector3.Cross(fwd, Vector3.up);
            transform.position = ballPosition + left * 0.75f - fwd * 0.15f;
            transform.rotation = Quaternion.LookRotation(-left, Vector3.up);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", identity);
            _body.SetPropertyBlock(block);
            _cap.SetPropertyBlock(block);
        }

        public void Show(bool visible) => gameObject.SetActive(visible);
    }

    /// <summary>The 20-lane fan from the ball, plus the preview guide at a labeled preview power.</summary>
    public sealed class AimFanView : MonoBehaviour
    {
        private readonly LineRenderer[] _lanes = new LineRenderer[LaneMapper.LaneCount];
        private LineRenderer _guide;
        private LineRenderer _aimLine;

        public static AimFanView Create(Transform parent, Material lineMaterial)
        {
            var root = new GameObject("Aim Fan");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<AimFanView>();
            for (int i = 0; i < view._lanes.Length; i++) view._lanes[i] = NewLine(root.transform, $"Lane {i + 1}", lineMaterial, 0.03f);
            view._guide = NewLine(root.transform, "Preview Guide", lineMaterial, 0.07f);
            view._aimLine = NewLine(root.transform, "Aim Line", lineMaterial, 0.05f);
            return view;
        }

        public void Show(bool visible) => gameObject.SetActive(visible);

        /// <param name="detectedLane">Lane frozen by the accepted input, or 0.</param>
        public void Draw(Vector3 ball, Vector3 aimForward, ClubDefinition club, int detectedLane, ShotResult preview, float length)
        {
            Vector3 fwd = new Vector3(aimForward.x, 0, aimForward.z).normalized;
            float half = (float)Units.DegToRad(club.FanHalfAngleDeg);
            Vector3 origin = ball + Vector3.up * 0.05f;
            for (int i = 0; i < _lanes.Length; i++)
            {
                int lane = i + 1;
                float yaw = (float)LaneMapper.RawYawRad(lane, half);
                Vector3 dir = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * fwd;
                LineRenderer lr = _lanes[i];
                bool detected = lane == detectedLane;
                bool center = lane == 10 || lane == 11;
                Color c = detected ? Palette.Cyan : center ? Color.white.WithAlpha(0.75f) : Color.white.WithAlpha(0.28f);
                lr.startColor = c;
                lr.endColor = c.WithAlpha(0f);
                lr.widthMultiplier = detected ? 0.09f : 0.03f;
                lr.positionCount = 2;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, origin + dir * length);
            }

            _aimLine.positionCount = 2;
            _aimLine.startColor = Palette.Cyan.WithAlpha(0.9f);
            _aimLine.endColor = Palette.Cyan.WithAlpha(0.2f);
            _aimLine.SetPosition(0, origin);
            _aimLine.SetPosition(1, origin + fwd * length * 1.3f);

            if (preview == null || detectedLane != 0)
            {
                _guide.positionCount = 0;
                return;
            }
            _guide.startColor = Palette.Cyan.WithAlpha(0.95f);
            _guide.endColor = Palette.Cyan.WithAlpha(0.35f);
            int n = preview.Samples.Count;
            _guide.positionCount = n;
            for (int i = 0; i < n; i++) _guide.SetPosition(i, preview.Samples[i].p.ToUnity() + Vector3.up * 0.06f);
        }

        private static LineRenderer NewLine(Transform parent, string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = material;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.useWorldSpace = true;
            lr.positionCount = 0;
            return lr;
        }
    }

    public enum CameraMode
    {
        Attract,
        Flyover,
        Setup,
        Putting,
        Follow,
        Result,
    }

    /// <summary>Camera reads shot state only; it never changes a trajectory to keep the ball on screen.</summary>
    public sealed class GolfCameraRig : MonoBehaviour
    {
        public CameraMode Mode;
        public bool ReducedMotion;

        private Camera _camera;
        private Vector3 _focus;
        private Vector3 _aim;
        private Vector3 _cup;
        private Transform _follow;
        private Vector3 _velocity;
        private float _orbit;

        public Camera Camera => _camera;

        public void Bind(Camera cam)
        {
            _camera = cam;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 1500f;
            _camera.fieldOfView = 50f;
        }

        public void SetScene(Vector3 ball, Vector3 aimForward, Vector3 cup)
        {
            _focus = ball;
            _aim = new Vector3(aimForward.x, 0, aimForward.z).normalized;
            if (_aim.sqrMagnitude < 0.5f) _aim = Vector3.forward;
            _cup = cup;
        }

        public void FollowTarget(Transform target) => _follow = target;

        public void Snap()
        {
            ComputeGoal(out Vector3 pos, out Vector3 look);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            _velocity = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            if (Mode == CameraMode.Attract || Mode == CameraMode.Flyover) _orbit += UnityEngine.Time.deltaTime * (ReducedMotion ? 2f : 6f);
            ComputeGoal(out Vector3 pos, out Vector3 look);
            float smooth = Mode == CameraMode.Follow ? 0.35f : 0.6f;
            if (ReducedMotion) smooth *= 1.5f;
            transform.position = Vector3.SmoothDamp(transform.position, pos, ref _velocity, smooth);
            Quaternion goal = Quaternion.LookRotation(look - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, goal, 1 - Mathf.Exp(-6f * UnityEngine.Time.deltaTime));
        }

        private void ComputeGoal(out Vector3 pos, out Vector3 look)
        {
            switch (Mode)
            {
                case CameraMode.Attract:
                case CameraMode.Flyover:
                {
                    Vector3 mid = Vector3.Lerp(_focus, _cup, 0.55f);
                    Quaternion rot = Quaternion.AngleAxis(_orbit, Vector3.up);
                    pos = mid + rot * new Vector3(0, 55, -95);
                    look = mid;
                    break;
                }
                case CameraMode.Putting:
                    pos = _focus - _aim * 3.2f + Vector3.up * 1.25f;
                    look = _focus + _aim * 4f;
                    break;
                case CameraMode.Follow:
                {
                    Vector3 target = _follow != null ? _follow.position : _focus;
                    pos = target - _aim * 14f + Vector3.up * 6f;
                    look = target + _aim * 6f;
                    break;
                }
                case CameraMode.Result:
                {
                    Vector3 target = _follow != null ? _follow.position : _focus;
                    pos = target - _aim * 9f + Vector3.up * 7f + Vector3.Cross(Vector3.up, _aim) * 4f;
                    look = target;
                    break;
                }
                default:
                    pos = _focus - _aim * 7.5f + Vector3.up * 2.6f;
                    look = _focus + _aim * 40f + Vector3.up * 2f;
                    break;
            }
        }
    }
}
