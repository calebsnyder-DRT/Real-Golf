using System;
using RealGolf.Core;
using RealGolf.Course;
using RealGolf.Data;
using UnityEngine;

namespace RealGolf.Gameplay
{
    public struct BallRestInfo
    {
        public Vector3 Position;
        public SurfaceType Surface;
        public bool Holed;
        public bool Penalty;
        public string PenaltyReason;
    }

    /// <summary>
    /// Ball flight and roll. Aerodynamic drag is kept deliberately light so that
    /// a club's advertised carry distance stays predictable, while backspin lift
    /// and surface-dependent rolling resistance supply the golf feel.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GolfBall : MonoBehaviour
    {
        public const float Radius = 0.03f;

        // Shared with BallisticSolver so calibrated launch speeds match real flight.
        public const float DragCoefficient = 0.0009f;
        public const float LiftCoefficient = 0.0011f;
        public const float SpinDecay = 0.35f;

        private const float RestSpeedThreshold = 0.22f;
        private const float RestTimeRequired = 0.55f;
        private const float MaxSettleTime = 22f;

        public event Action<BallRestInfo> CameToRest;

        private Rigidbody _rb;
        private HoleTerrain _terrain;
        private Vector3 _cupPosition;
        private Wind _wind;
        private TrailRenderer _tracer;
        private GolfAudio _audio;

        private bool _live;
        private float _spin;
        private float _restTimer;
        private float _liveTimer;

        public bool IsLive => _live;
        public SurfaceType CurrentSurface { get; private set; } = SurfaceType.TeeBox;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => _rb != null ? _rb.linearVelocity : Vector3.zero;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = 0.04593f;
            _rb.useGravity = true;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.6f;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Configure(HoleTerrain terrain, Vector3 cupPosition)
        {
            _terrain = terrain;
            _cupPosition = cupPosition;
        }

        /// <summary>
        /// One-time setup for things that belong to the owning player rather than
        /// the hole: the tracer colour and the shared audio bench.
        /// </summary>
        public void Initialise(int playerIndex, GolfAudio audio)
        {
            _audio = audio;

            _tracer = gameObject.AddComponent<TrailRenderer>();
            _tracer.material = MaterialLibrary.Tracer(playerIndex);
            _tracer.time = 2.6f;
            _tracer.minVertexDistance = 0.35f;
            _tracer.numCapVertices = 4;
            _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tracer.receiveShadows = false;
            // Tapers to nothing so the line reads as a trail rather than a tube.
            _tracer.widthCurve = AnimationCurve.EaseInOut(0f, 0.09f, 1f, 0f);
            _tracer.emitting = false;
            _tracer.Clear();
        }

        public void SetWind(Wind wind) => _wind = wind;

        private void SetTracing(bool on)
        {
            if (_tracer == null)
            {
                return;
            }

            if (!on)
            {
                _tracer.emitting = false;
                return;
            }

            _tracer.Clear();
            _tracer.emitting = true;
        }

        public void PlaceAt(Vector3 position, bool onTee)
        {
            _live = false;
            _spin = 0f;
            _restTimer = 0f;
            _liveTimer = 0f;

            _rb.isKinematic = true;
            transform.position = position + Vector3.up * (Radius + (onTee ? 0.03f : 0.001f));
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.Sleep();

            SetTracing(false);
            CurrentSurface = SampleSurface();
        }

        public void Launch(Vector3 velocity, float spin)
        {
            _rb.isKinematic = false;
            _rb.WakeUp();
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Vector3.zero;

            _spin = spin;
            _live = true;
            _restTimer = 0f;
            _liveTimer = 0f;

            // A tracer skidding along the green behind a putt just looks like a smear.
            SetTracing(velocity.y > 1.5f);
        }

        private void FixedUpdate()
        {
            if (!_live)
            {
                return;
            }

            _liveTimer += Time.fixedDeltaTime;
            Vector3 velocity = _rb.linearVelocity;
            float speed = velocity.magnitude;

            bool grounded = IsGrounded();

            // Drag acts against motion through the air, not across the ground, so
            // wind enters the simulation here rather than as a separate push.
            Vector3 airVelocity = velocity;
            if (!grounded && _wind != null)
            {
                airVelocity -= _wind.Velocity;
            }

            float airSpeed = airVelocity.magnitude;
            if (airSpeed > 0.01f)
            {
                Vector3 airDirection = airVelocity / airSpeed;
                _rb.AddForce(-airDirection * (DragCoefficient * airSpeed * airSpeed), ForceMode.Acceleration);
            }

            if (!grounded && Mathf.Abs(_spin) > 0.01f && speed > 0.01f)
            {
                // Magnus force, perpendicular to travel: backspin holds the ball up,
                // and negative spin presses it down into a boring flight.
                Vector3 direction = velocity / speed;
                Vector3 lateral = Vector3.Cross(direction, Vector3.up);
                Vector3 liftDirection = Vector3.Cross(lateral, direction).normalized;
                _rb.AddForce(liftDirection * (LiftCoefficient * _spin * speed * speed), ForceMode.Acceleration);
            }

            _spin = Mathf.MoveTowards(_spin, 0f, SpinDecay * Time.fixedDeltaTime);

            if (grounded)
            {
                ApplyRollingResistance();
            }

            EvaluateState(speed, grounded);
        }

        private void ApplyRollingResistance()
        {
            CurrentSurface = SampleSurface();
            float damping = CurrentSurface.RollDrag();
            Vector3 velocity = _rb.linearVelocity;

            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, damping * Time.fixedDeltaTime);
            _rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        private void EvaluateState(float speed, bool grounded)
        {
            SurfaceType surface = SampleSurface();

            if (surface == SurfaceType.Water && transform.position.y < WaterCheckHeight())
            {
                Finish(new BallRestInfo
                {
                    Position = transform.position,
                    Surface = SurfaceType.Water,
                    Penalty = true,
                    PenaltyReason = "In the water"
                });
                return;
            }

            if (surface == SurfaceType.OutOfBounds)
            {
                Finish(new BallRestInfo
                {
                    Position = transform.position,
                    Surface = SurfaceType.OutOfBounds,
                    Penalty = true,
                    PenaltyReason = "Out of bounds"
                });
                return;
            }

            if (IsInCup(speed))
            {
                Finish(new BallRestInfo
                {
                    Position = _cupPosition,
                    Surface = SurfaceType.Green,
                    Holed = true
                });
                return;
            }

            if (speed < RestSpeedThreshold && grounded)
            {
                _restTimer += Time.fixedDeltaTime;
                if (_restTimer >= RestTimeRequired)
                {
                    Finish(new BallRestInfo { Position = transform.position, Surface = surface });
                }
            }
            else
            {
                _restTimer = 0f;
            }

            // Safety valve for a ball wedged against geometry.
            if (_liveTimer > MaxSettleTime)
            {
                Finish(new BallRestInfo { Position = transform.position, Surface = surface });
            }
        }

        private bool IsInCup(float speed)
        {
            Vector3 delta = transform.position - _cupPosition;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;

            // Ball must be near the cup, low, and slow enough to drop rather than lip out.
            return horizontal <= HoleBuilder.CupCaptureRadius
                   && Mathf.Abs(delta.y) < 0.35f
                   && speed < 4.2f;
        }

        private float WaterCheckHeight()
        {
            if (_terrain == null)
            {
                return float.MinValue;
            }

            var planar = new Vector2(transform.position.x, transform.position.z);
            return _terrain.SampleGroundHeight(planar) - 0.4f;
        }

        private void Finish(BallRestInfo info)
        {
            if (!_live)
            {
                return;
            }

            _live = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.Sleep();

            SetTracing(false);

            if (_audio != null)
            {
                if (info.Holed)
                {
                    _audio.PlayHoled();
                }
                else if (info.Surface == SurfaceType.Water)
                {
                    _audio.PlaySplash();
                }
            }

            CurrentSurface = info.Surface;
            CameToRest?.Invoke(info);
        }

        private bool IsGrounded() =>
            Physics.Raycast(transform.position, Vector3.down, Radius + 0.06f, ~0, QueryTriggerInteraction.Ignore);

        private SurfaceType SampleSurface()
        {
            if (_terrain == null)
            {
                return SurfaceType.Fairway;
            }

            return _terrain.SampleSurface(new Vector2(transform.position.x, transform.position.z));
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!_live)
            {
                return;
            }

            SurfaceType surface = SampleSurface();
            Vector3 velocity = _rb.linearVelocity;
            Vector3 normal = collision.GetContact(0).normal;

            Vector3 normalComponent = Vector3.Project(velocity, normal);
            Vector3 tangential = velocity - normalComponent;

            float restitution = surface.Bounciness();
            float friction = surface == SurfaceType.Bunker ? 0.35f : 0.78f;

            // Spin bites on the first bounce: backspin checks the ball up, topspin
            // releases it forward. This is what makes shot shaping worth doing.
            friction *= Mathf.Clamp(1f - _spin * 0.28f, 0.4f, 1.5f);

            _rb.linearVelocity = tangential * friction - normalComponent * restitution;
            _spin *= 0.45f;

            // Only sound the impacts you would actually hear, so a ball trickling
            // across the fairway does not chatter.
            float impact = normalComponent.magnitude;
            if (_audio != null && impact > 1.6f)
            {
                _audio.PlayBounce(Mathf.InverseLerp(1.6f, 18f, impact));
            }
        }
    }
}
