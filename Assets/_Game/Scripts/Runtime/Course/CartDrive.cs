using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Domain;
using Puttshack.Runtime.Shop;
using Puttshack.Runtime.Views;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// The between-holes transition: the next hole's first player drives the golf cart along the cart path from the green
    /// they just finished to the next tee while a chase camera follows and finally looks down the new hole. Purely
    /// presentation: <see cref="GameHost"/> holds the session in HOLE_LOAD until the drive finishes or is skipped.
    /// Between drives the cart waits by the current tee until the hole's first shot.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CartDrive : MonoBehaviour
    {
        public const string ModelPath = "Models/Course/Cart/GolfCart";
        private const float CruiseSpeed = 10f;
        private const float MinSeconds = 4.5f;
        private const float MaxSeconds = 7.5f;
        private const float RampFraction = 0.2f;
        private const float BlendInSeconds = 1.0f;
        private const float RevealSeconds = 1.7f;
        private const float PathLift = 0.05f;

        private Transform _cart;
        private Transform[] _wheels = new Transform[4];
        private Quaternion[] _wheelBase = new Quaternion[4];
        private Transform _steering;
        private Quaternion _steeringBase;
        private Vector3 _steeringAxis = Vector3.up;
        private Transform _bagMount;
        private float _wheelRadius = 0.23f;
        private float _wheelbase = 1.64f;
        private float _track = 1.0f;
        private float _driverSide = -1f;
        private SeatedDriverPose _pose;
        private string _parked;

        private readonly List<Vector3> _route = new List<Vector3>();
        private readonly List<float> _distance = new List<float>();
        private HoleDefinition _terrainHole;
        private HoleDefinition _to;
        private float _length, _duration, _t;
        private float _spin, _steer;
        private Camera _camera;
        private GolfCameraRig _rig;
        private CourseGolfer _golfer;
        private Vector3 _camStartPos;
        private Quaternion _camStartRot;
        private Vector3 _chasePos, _chaseVel, _lookPos, _lookVel;

        public bool Active { get; private set; }
        public bool HasCart => _cart != null;
        public float Duration => _duration;

        public static CartDrive Create(Transform parent)
        {
            var model = Resources.Load<GameObject>(ModelPath);
            var go = new GameObject("Cart Drive");
            go.transform.SetParent(parent, false);
            var drive = go.AddComponent<CartDrive>();
            if (model != null) drive.Bind(Instantiate(model, go.transform));
            return drive;
        }

        private void Bind(GameObject cart)
        {
            cart.name = "Golf Cart";
            _cart = cart.transform;
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                _wheels[i] = Find(_cart, names[i]);
                if (_wheels[i] != null) _wheelBase[i] = _wheels[i].localRotation;
            }
            if (_wheels[0] != null && _wheels[2] != null)
            {
                _wheelRadius = Mathf.Max(0.1f, _cart.InverseTransformPoint(_wheels[0].position).y);
                _wheelbase = Mathf.Max(0.5f, _cart.InverseTransformPoint(_wheels[0].position).z - _cart.InverseTransformPoint(_wheels[2].position).z);
            }
            if (_wheels[0] != null && _wheels[1] != null)
                _track = Mathf.Max(0.4f, Mathf.Abs(_cart.InverseTransformPoint(_wheels[1].position).x - _cart.InverseTransformPoint(_wheels[0].position).x));
            _steering = Find(_cart, "Steering_Wheel");
            Transform up = Find(_cart, "Steering_Up");
            if (_steering != null)
            {
                _steeringBase = _steering.localRotation;
                if (up != null && _steering.parent != null)
                {
                    Vector3 axis = _steering.parent.InverseTransformDirection(up.position - _steering.position);
                    if (axis.sqrMagnitude > 1e-6f) _steeringAxis = axis.normalized;
                }
            }
            _bagMount = Find(_cart, "Bag_Mount");

            _pose = gameObject.AddComponent<SeatedDriverPose>();
            _pose.Frame = _cart;
            _pose.Seat = Find(_cart, "Seat_Driver");
            if (_pose.Seat != null) _driverSide = Mathf.Sign(_cart.InverseTransformPoint(_pose.Seat.position).x);
            _pose.FootL = Find(_cart, "Foot_L");
            _pose.FootR = Find(_cart, "Foot_R");
            _pose.HandL = Find(_cart, "Hand_L");
            _pose.HandR = Find(_cart, "Hand_R");
            _pose.enabled = false;
            cart.SetActive(false);
        }

        /// <summary>Waits by the hole's tee (shown only until the hole's first shot; hidden while no hole is loaded).</summary>
        public void Park(HoleDefinition hole, bool visible)
        {
            if (_cart == null || Active) return;
            if (!visible || hole == null)
            {
                if (_cart.gameObject.activeSelf) _cart.gameObject.SetActive(false);
                _parked = null;
                return;
            }
            if (_parked == hole.Id) return;
            _parked = hole.Id;
            Vec2d park = CourseLayout.TeePark(hole);
            int k = CourseLayout.Nearest(park.x, park.z);
            IReadOnlyList<Vec2d> loop = CourseLayout.CartPath;
            Vec2d a = loop[(k + loop.Count - 1) % loop.Count], b = loop[(k + 1) % loop.Count];
            Vector3 heading = new Vector3((float)(b.x - a.x), 0, (float)(b.z - a.z));
            Vector3 tee = hole.TeeM.ToUnity();
            Vector3 toTee = new Vector3(tee.x - (float)park.x, 0, tee.z - (float)park.z);
            // Nose pointing along the path in whichever direction leans toward the tee.
            if (Vector3.Dot(heading, toTee) < 0) heading = -heading;
            _terrainHole = hole;
            if (!_cart.gameObject.activeSelf) _cart.gameObject.SetActive(true);
            PlaceCart(new Vector3((float)park.x, 0, (float)park.z), heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.forward);
            SpinWheels(0, 0);
        }

        /// <summary>Starts the drive from <paramref name="from"/>'s green to <paramref name="to"/>'s tee. False without a cart.</summary>
        public bool Begin(HoleDefinition from, HoleDefinition to, PlayerState driver, CourseGolfer golfer, Camera camera, GolfCameraRig rig, bool reducedMotion)
        {
            if (_cart == null || from == null || to == null) return false;
            BuildRoute(CourseLayout.TransitionRoute(from, to), to);
            if (_length < 5f) return false;

            _to = to;
            _parked = null;
            _terrainHole = to;
            _duration = Mathf.Clamp(_length / CruiseSpeed, MinSeconds, MaxSeconds);
            _t = 0f;
            _spin = 0f;
            _steer = 0f;
            _cart.gameObject.SetActive(true);
            Sample(0f, out Vector3 p0, out Vector3 f0);
            PlaceCart(p0, f0);

            _golfer = golfer;
            if (_golfer != null)
            {
                _golfer.Show(true);
                _golfer.Stand(p0, Vector3.Cross(f0, Vector3.up), driver, null, address: false, wait: true);
                _pose.Root = _golfer.transform;
                _pose.Bag = _golfer.Bag != null ? _golfer.Bag.transform : null;
                _pose.BagMount = _bagMount;
                _pose.Animator = _golfer.Appearance != null ? _golfer.Appearance.ShownAnimator : null;
                _pose.enabled = _pose.Seat != null && _pose.Animator != null;
            }

            _camera = camera;
            _rig = rig;
            if (_camera != null)
            {
                if (_rig != null) _rig.enabled = false;
                _camStartPos = _camera.transform.position;
                _camStartRot = _camera.transform.rotation;
                ChaseGoal(p0, f0, out _chasePos, out _lookPos);
                _chaseVel = _lookVel = Vector3.zero;
            }
            Active = true;
            if (reducedMotion) Skip();
            return true;
        }

        /// <summary>Jumps to the end of the drive: the cart parked by the tee.</summary>
        public void Skip()
        {
            if (!Active) return;
            _t = _duration;
            Finish();
        }

        private void Update()
        {
            if (!Active) return;
            float dt = UnityEngine.Time.deltaTime;
            float before = Travel(_t);
            _t = Mathf.Min(_t + dt, _duration);
            float s = Travel(_t);
            Sample(s, out Vector3 pos, out Vector3 fwd);
            PlaceCart(pos, fwd);

            Sample(Mathf.Min(s + 2.5f, _length), out _, out Vector3 ahead);
            float yaw = Vector3.SignedAngle(fwd, ahead, Vector3.up) * Mathf.Deg2Rad;
            float steerGoal = Mathf.Clamp(Mathf.Atan2(_wheelbase * yaw, 2.5f) * Mathf.Rad2Deg, -28f, 28f);
            _steer = Mathf.Lerp(_steer, steerGoal, 1f - Mathf.Exp(-8f * dt));
            SpinWheels(s - before, _steer);

            if (_t >= _duration) Finish();
        }

        private void LateUpdate()
        {
            if (!Active || _camera == null) return;
            Sample(Travel(_t), out Vector3 pos, out Vector3 fwd);
            ChaseGoal(pos, fwd, out Vector3 chase, out Vector3 look);
            float dt = UnityEngine.Time.deltaTime;
            _chasePos = Vector3.SmoothDamp(_chasePos, chase, ref _chaseVel, 0.35f, Mathf.Infinity, dt);
            _lookPos = Vector3.SmoothDamp(_lookPos, look, ref _lookVel, 0.2f, Mathf.Infinity, dt);
            Vector3 camPos = _chasePos;
            Quaternion camRot = Quaternion.LookRotation(_lookPos - _chasePos, Vector3.up);

            float reveal = Smooth((_t - (_duration - RevealSeconds)) / RevealSeconds);
            if (reveal > 0f)
            {
                RevealPose(out Vector3 rp, out Quaternion rr);
                camPos = Vector3.Lerp(camPos, rp, reveal);
                camRot = Quaternion.Slerp(camRot, rr, reveal);
            }
            float blend = Smooth(_t / BlendInSeconds);
            _camera.transform.SetPositionAndRotation(Vector3.Lerp(_camStartPos, camPos, blend), Quaternion.Slerp(_camStartRot, camRot, blend));
        }

        private void Finish()
        {
            Sample(_length, out Vector3 pos, out Vector3 fwd);
            PlaceCart(pos, fwd);
            SpinWheels(0, 0);
            Active = false;
            _parked = _to.Id;
            if (_pose != null) _pose.enabled = false;
            if (_golfer != null) _golfer.Show(false);
            _golfer = null;
            if (_camera != null)
            {
                RevealPose(out Vector3 rp, out Quaternion rr);
                _camera.transform.SetPositionAndRotation(rp, rr);
            }
            if (_rig != null) _rig.enabled = true;
        }

        // ---------------------------------------------------------------- route

        private void BuildRoute(List<Vec2d> line, HoleDefinition terrain)
        {
            var pts = new List<Vector3>(line.Count);
            foreach (Vec2d p in line) pts.Add(new Vector3((float)p.x, 0, (float)p.z));
            // Corner cutting rounds the step off the path onto the tee apron.
            for (int pass = 0; pass < 2 && pts.Count > 2; pass++)
            {
                var cut = new List<Vector3> { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    cut.Add(Vector3.Lerp(pts[i], pts[i + 1], 0.25f));
                    cut.Add(Vector3.Lerp(pts[i], pts[i + 1], 0.75f));
                }
                cut.Add(pts[pts.Count - 1]);
                pts = cut;
            }
            _route.Clear();
            _distance.Clear();
            float d = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) d += Vector3.Distance(pts[i - 1], pts[i]);
                _route.Add(pts[i]);
                _distance.Add(d);
            }
            _length = d;
            _terrainHole = terrain;
        }

        /// <summary>Distance along the route at time t: ease in, cruise, ease out.</summary>
        private float Travel(float t)
        {
            if (_duration <= 0f) return _length;
            float u = Mathf.Clamp01(t / _duration), r = RampFraction;
            float area = 1f - r;
            float f;
            if (u < r) f = u * u / (2f * r);
            else if (u > 1f - r) f = area - (1f - u) * (1f - u) / (2f * r);
            else f = r * 0.5f + (u - r);
            return _length * f / area;
        }

        private void Sample(float s, out Vector3 pos, out Vector3 fwd)
        {
            s = Mathf.Clamp(s, 0f, _length);
            int i = 1;
            while (i < _distance.Count - 1 && _distance[i] < s) i++;
            float seg = Mathf.Max(1e-4f, _distance[i] - _distance[i - 1]);
            pos = Vector3.Lerp(_route[i - 1], _route[i], (s - _distance[i - 1]) / seg);
            Vector3 a = Position(Mathf.Max(0f, s - 1.2f)), b = Position(Mathf.Min(_length, s + 1.2f));
            fwd = b - a;
            fwd.y = 0;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        }

        private Vector3 Position(float s)
        {
            int i = 1;
            while (i < _distance.Count - 1 && _distance[i] < s) i++;
            float seg = Mathf.Max(1e-4f, _distance[i] - _distance[i - 1]);
            return Vector3.Lerp(_route[i - 1], _route[i], Mathf.Clamp01((s - _distance[i - 1]) / seg));
        }

        // ---------------------------------------------------------------- cart

        private float Ground(float x, float z) =>
            _terrainHole != null ? (float)TerrainModel.Height(_terrainHole.Terrain, x, z) + PathLift : 0f;

        /// <summary>Sits the cart on the ground: height from the four wheel contacts, pitch and roll from their slopes.</summary>
        private void PlaceCart(Vector3 at, Vector3 fwd)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float hw = _wheelbase * 0.5f, ht = _track * 0.5f;
            Vector3 fl = at + fwd * hw - right * ht, fr = at + fwd * hw + right * ht;
            Vector3 rl = at - fwd * hw - right * ht, rr = at - fwd * hw + right * ht;
            float hfl = Ground(fl.x, fl.z), hfr = Ground(fr.x, fr.z), hrl = Ground(rl.x, rl.z), hrr = Ground(rr.x, rr.z);
            float front = (hfl + hfr) * 0.5f, rear = (hrl + hrr) * 0.5f;
            float leftH = (hfl + hrl) * 0.5f, rightH = (hfr + hrr) * 0.5f;
            Vector3 forward3 = (fwd * _wheelbase + Vector3.up * (front - rear)).normalized;
            Vector3 right3 = (right * _track + Vector3.up * (rightH - leftH)).normalized;
            Vector3 up = Vector3.Cross(forward3, right3).normalized;
            _cart.SetPositionAndRotation(new Vector3(at.x, (front + rear) * 0.5f, at.z), Quaternion.LookRotation(forward3, up));
        }

        private void SpinWheels(float travelled, float steer)
        {
            _spin = (_spin + travelled / _wheelRadius * Mathf.Rad2Deg) % 360f;
            for (int i = 0; i < 4; i++)
            {
                if (_wheels[i] == null) continue;
                Quaternion yaw = i < 2 ? Quaternion.Euler(0, steer, 0) : Quaternion.identity;
                _wheels[i].localRotation = yaw * _wheelBase[i] * Quaternion.Euler(_spin, 0, 0);
            }
            if (_steering != null) _steering.localRotation = Quaternion.AngleAxis(-steer * 3.2f, _steeringAxis) * _steeringBase;
        }

        // ---------------------------------------------------------------- camera

        private void ChaseGoal(Vector3 pos, Vector3 fwd, out Vector3 camera, out Vector3 look)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float ground = Ground(pos.x, pos.z);
            // Off the driver's shoulder: from straight behind, the bag on the rear rack hides them.
            camera = new Vector3(pos.x, ground, pos.z) - fwd * 5.4f + right * (_driverSide * 3.8f) + Vector3.up * 2.3f;
            float under = Ground(camera.x, camera.z) + 1.2f;
            if (camera.y < under) camera.y = under;
            look = new Vector3(pos.x, ground, pos.z) + fwd * 5f + Vector3.up * 0.9f;
        }

        /// <summary>Behind and above the new tee, looking down the hole.</summary>
        private void RevealPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 tee = _to.TeeM.ToUnity();
            Vector3 fwd = CourseVisualBuilder.PlayDirection(_to);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            pos = tee - fwd * 14f + right * 3f + Vector3.up * 7f;
            float under = Ground(pos.x, pos.z) + 2f;
            if (pos.y < under) pos.y = under;
            Vector3 look = tee + fwd * 55f + Vector3.up * 1f;
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
