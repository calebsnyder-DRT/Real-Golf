using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Drives the main camera while the Pro Shop is open: an establishing overview that eases into the portrait
    /// framing (golfer centred, clothing wall behind, plinth right), then a very slow sway. Reduced motion cuts
    /// straight to the portrait and holds still. Marker transforms come from the room model's Cam_* empties.
    /// </summary>
    public sealed class ProShopCameraRig : MonoBehaviour
    {
        public Transform Portrait;
        public Transform PortraitTarget;
        public Transform Overview;
        public Transform OverviewTarget;
        public float PortraitFov = 32f;
        public float OverviewFov = 42f;
        public float IntroSeconds = 1.6f;
        [Tooltip("Peak sideways sway in degrees around the golfer; 0 disables it.")]
        public float SwayDegrees = 1.2f;
        public float SwayPeriodSeconds = 14f;

        private Camera _camera;
        private float _blend;
        private float _time;
        private bool _holdOverview;
        private float _savedNear, _savedFar, _savedFov;

        public bool Active => _camera != null;
        public bool ReducedMotion { get; set; }
        public float Blend => _blend;

        public void Begin(Camera cam, bool reducedMotion)
        {
            _camera = cam;
            _savedNear = cam.nearClipPlane;
            _savedFar = cam.farClipPlane;
            _savedFov = cam.fieldOfView;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            ReducedMotion = reducedMotion;
            _blend = reducedMotion ? 1f : 0f;
            _time = 0f;
            _holdOverview = false;
            Apply();
        }

        public void End()
        {
            if (_camera == null) return;
            _camera.nearClipPlane = _savedNear;
            _camera.farClipPlane = _savedFar;
            _camera.fieldOfView = _savedFov;
            _camera = null;
        }

        /// <summary>Holds the establishing shot (review captures) until released.</summary>
        public void HoldOverview(bool hold)
        {
            _holdOverview = hold;
            if (hold) _blend = 0f;
            Apply();
        }

        public void SnapToPortrait()
        {
            _holdOverview = false;
            _blend = 1f;
            Apply();
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            float dt = UnityEngine.Time.unscaledDeltaTime;
            _time += dt;
            if (!_holdOverview) _blend = Mathf.MoveTowards(_blend, 1f, ReducedMotion ? 1f : dt / Mathf.Max(0.01f, IntroSeconds));
            Apply();
        }

        private void Apply()
        {
            if (_camera == null || Portrait == null || PortraitTarget == null) return;
            float k = Mathf.SmoothStep(0f, 1f, _blend);
            Vector3 fromPos = Overview != null ? Overview.position : Portrait.position;
            Vector3 fromLook = OverviewTarget != null ? OverviewTarget.position : PortraitTarget.position;
            Vector3 look = Vector3.Lerp(fromLook, PortraitTarget.position, k);
            Vector3 pos = Vector3.Lerp(fromPos, Portrait.position, k);
            if (!ReducedMotion && SwayDegrees > 0f)
            {
                float yaw = Mathf.Sin(_time * Mathf.PI * 2f / Mathf.Max(1f, SwayPeriodSeconds)) * SwayDegrees * k;
                pos = PortraitTarget.position + Quaternion.AngleAxis(yaw, Vector3.up) * (pos - PortraitTarget.position);
            }
            _camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            _camera.fieldOfView = Mathf.Lerp(OverviewFov, PortraitFov, k);
        }
    }
}
