using UnityEngine;

namespace RealGolf.CameraRig
{
    public enum CameraMode
    {
        Aim,
        Flight,
        Overhead,
        HoleIntro
    }

    /// <summary>
    /// Follows play: sits behind the ball while aiming, tracks it through the
    /// air, and can pull back to an overhead read of the hole.
    /// </summary>
    public sealed class GolfCamera : MonoBehaviour
    {
        private const float AimDistance = 8.5f;
        private const float AimHeight = 3.4f;
        private const float PuttDistance = 3.6f;
        private const float PuttHeight = 1.5f;
        private const float FlightDistance = 13f;
        private const float FlightHeight = 5.5f;

        public CameraMode Mode { get; private set; } = CameraMode.Aim;

        private Transform _ball;
        private Vector3 _aimDirection = Vector3.forward;
        private Vector3 _ballPosition;
        private bool _putting;

        private Vector3 _overheadCenter;
        private float _overheadHeight = 120f;

        private Vector3 _introStart;
        private Vector3 _introEnd;
        private Vector3 _introLookAt;
        private float _introTime;
        private float _introDuration = 4f;

        private Vector3 _positionVelocity;
        private Vector3 _currentLookAt;

        public void SetAim(Transform ball, Vector3 ballPosition, Vector3 aimDirection, bool putting)
        {
            _ball = ball;
            _ballPosition = ballPosition;
            _aimDirection = aimDirection.normalized;
            _putting = putting;
            Mode = CameraMode.Aim;
        }

        public void UpdateAim(Vector3 ballPosition, Vector3 aimDirection)
        {
            _ballPosition = ballPosition;
            _aimDirection = aimDirection.normalized;
        }

        public void SetFlight(Transform ball)
        {
            _ball = ball;
            Mode = CameraMode.Flight;
        }

        public void SetOverhead(Vector3 center, float height)
        {
            _overheadCenter = center;
            _overheadHeight = height;
            Mode = CameraMode.Overhead;
        }

        public void SetHoleIntro(Vector3 teePosition, Vector3 greenPosition, float duration)
        {
            Vector3 direction = greenPosition - teePosition;
            direction.y = 0f;
            direction.Normalize();

            _introStart = greenPosition + Vector3.up * 26f - direction * 22f;
            _introEnd = teePosition + Vector3.up * 12f - direction * 18f;
            _introLookAt = Vector3.Lerp(teePosition, greenPosition, 0.5f);
            _introDuration = duration;
            _introTime = 0f;

            Mode = CameraMode.HoleIntro;
            transform.position = _introStart;
            transform.LookAt(_introLookAt);
            _currentLookAt = _introLookAt;
        }

        /// <summary>Snap straight to the framing for the current mode, skipping easing.</summary>
        public void SnapToTarget()
        {
            ComputeTarget(out Vector3 position, out Vector3 lookAt);
            transform.position = position;
            _currentLookAt = lookAt;
            transform.LookAt(lookAt);
            _positionVelocity = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (Mode == CameraMode.HoleIntro)
            {
                _introTime += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_introTime / _introDuration));
                transform.position = Vector3.Lerp(_introStart, _introEnd, t);
                transform.LookAt(_introLookAt);
                return;
            }

            ComputeTarget(out Vector3 targetPosition, out Vector3 targetLookAt);

            float smoothTime = Mode == CameraMode.Flight ? 0.22f : 0.16f;
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _positionVelocity, smoothTime);

            _currentLookAt = Vector3.Lerp(_currentLookAt, targetLookAt, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.LookAt(_currentLookAt);
        }

        private void ComputeTarget(out Vector3 position, out Vector3 lookAt)
        {
            switch (Mode)
            {
                case CameraMode.Overhead:
                    position = _overheadCenter + Vector3.up * _overheadHeight;
                    lookAt = _overheadCenter + Vector3.forward * 0.01f;
                    return;

                case CameraMode.Flight when _ball != null:
                {
                    Vector3 ballPosition = _ball.position;
                    Vector3 back = -_aimDirection;
                    position = ballPosition + back * FlightDistance + Vector3.up * FlightHeight;
                    lookAt = ballPosition;
                    return;
                }

                default:
                {
                    Vector3 ballPosition = _ball != null ? _ball.position : _ballPosition;
                    float distance = _putting ? PuttDistance : AimDistance;
                    float height = _putting ? PuttHeight : AimHeight;

                    position = ballPosition - _aimDirection * distance + Vector3.up * height;
                    lookAt = ballPosition + _aimDirection * (_putting ? 2.5f : 7f) + Vector3.up * 0.4f;
                    return;
                }
            }
        }
    }
}
